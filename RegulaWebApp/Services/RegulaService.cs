using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RegulaWebApp.Models;

namespace RegulaWebApp.Services;

public class RegulaService : IRegulaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<RegulaOptions> _optionsAccessor;

    public RegulaService(
        IHttpClientFactory httpClientFactory,
        IOptions<RegulaOptions> optionsAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _optionsAccessor = optionsAccessor;
    }

    public async Task<IActionResult> DetectFace(HttpRequest request)
    {
        var imageBase64 = await ReadImageBase64Async(request);
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return new BadRequestObjectResult(new { error = "No image provided. Send multipart/form-data with 'image' or JSON with 'imageBase64'." });
        }

        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("Regula");

        var payload = new
        {
            tag = "detect-face",
            processParam = new
            {
                attributes = new
                {
                    config = new[]
                    {
                        new { name = "Age" },
                        new { name = "Sex" },
                        new { name = "Emotion" },
                        new { name = "Smile" },
                        new { name = "Mouth" },
                    }
                }
            },
            image = imageBase64
        };

        using var response = await client.PostAsJsonAsync(options.DetectEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "Regula detect request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var summary = ExtractFaceSummary(content);
        return new OkObjectResult(new
        {
            summary.Details,
            raw = JsonDocument.Parse(content).RootElement
        });
    }

    public async Task<IActionResult> LivenessDetection(LivenessRequest body)
    {
        body ??= new LivenessRequest();
        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("Regula");

        if (!string.IsNullOrWhiteSpace(body.TransactionId))
        {
            var url = options.LivenessEndpoint;
            url = url.Contains('?')
                ? $"{url}&transactionId={Uri.EscapeDataString(body.TransactionId)}"
                : $"{url}?transactionId={Uri.EscapeDataString(body.TransactionId)}";

            using var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return new ObjectResult(new { error = "Regula liveness request failed.", details = content })
                {
                    StatusCode = (int)response.StatusCode
                };
            }

            var livenessSummary = ExtractLivenessSummary(content);
            return new OkObjectResult(new
            {
                livenessSummary.LivenessStatus,
                livenessSummary.Score,
                raw = JsonDocument.Parse(content).RootElement
            });
        }

        if (body.Frames is null || body.Frames.Count == 0)
        {
            return new BadRequestObjectResult(new { error = "Provide 'transactionId' or an array of 'frames' (base64 images)." });
        }

        var payload = new
        {
            tag = "liveness",
            frames = body.Frames
        };

        using var framesResponse = await client.PostAsJsonAsync(options.LivenessEndpoint, payload);
        var framesContent = await framesResponse.Content.ReadAsStringAsync();
        if (!framesResponse.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "Regula liveness frames request failed.", details = framesContent })
            {
                StatusCode = (int)framesResponse.StatusCode
            };
        }

        var summary = ExtractLivenessSummary(framesContent);
        return new OkObjectResult(new
        {
            summary.LivenessStatus,
            summary.Score,
            raw = JsonDocument.Parse(framesContent).RootElement
        });
    }

    public async Task<IActionResult> FaceMatch(HttpRequest request)
    {
        var (image1, image2) = await ReadTwoImagesBase64Async(request);
        if (string.IsNullOrWhiteSpace(image1) || string.IsNullOrWhiteSpace(image2))
        {
            return new BadRequestObjectResult(new { error = "Provide two images. Send multipart/form-data with 'image1' and 'image2' or JSON with 'imageBase64_1' and 'imageBase64_2'." });
        }

        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("Regula");

        var payload = new
        {
            tag = "face-match",
            images = new[]
            {
                new { index = 0, type = 1, data = image1 },
                new { index = 1, type = 1, data = image2 }
            }
        };

        using var response = await client.PostAsJsonAsync(options.MatchEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "Regula match request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var summary = ExtractMatchSummary(content);
        return new OkObjectResult(new
        {
            summary.Similarity,
            summary.Score,
            raw = JsonDocument.Parse(content).RootElement
        });
    }

    public async Task<(double? similarity, int? statusCode, string? error, string? details)> MatchFaces(string image1, string image2)
    {
        if (string.IsNullOrWhiteSpace(image1) || string.IsNullOrWhiteSpace(image2))
        {
            return (null, 400, "Provide two images for face match.", null);
        }

        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("Regula");

        var payload = new
        {
            tag = "face-match",
            images = new[]
            {
                new { index = 0, type = 1, data = image1 },
                new { index = 1, type = 1, data = image2 }
            }
        };

        using var response = await client.PostAsJsonAsync(options.MatchEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return (null, (int)response.StatusCode, "Regula match request failed.", content);
        }

        var summary = ExtractMatchSummary(content);
        return (summary.Similarity, (int)response.StatusCode, null, null);
    }

    private static async Task<string?> ReadImageBase64Async(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null)
            {
                return null;
            }

            await using var stream = file.OpenReadStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        var body = await request.ReadFromJsonAsync<DetectFaceRequest>();
        return body?.ImageBase64;
    }

    private static async Task<(string? Image1, string? Image2)> ReadTwoImagesBase64Async(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var file1 = form.Files.GetFile("image1") ?? form.Files.ElementAtOrDefault(0);
            var file2 = form.Files.GetFile("image2") ?? form.Files.ElementAtOrDefault(1);
            return (await ReadFileAsBase64Async(file1), await ReadFileAsBase64Async(file2));
        }

        var body = await request.ReadFromJsonAsync<FaceMatchRequest>();
        return (body?.ImageBase64_1, body?.ImageBase64_2);
    }

    private static async Task<string?> ReadFileAsBase64Async(IFormFile? file)
    {
        if (file is null)
        {
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static FaceSummary ExtractFaceSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) &&
                results.TryGetProperty("detections", out var detections) &&
                detections.ValueKind == JsonValueKind.Array)
            {
                foreach (var detection in detections.EnumerateArray())
                {
                    if (!detection.TryGetProperty("attributes", out var attributes))
                    {
                        continue;
                    }

                    if (!attributes.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var detailsMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("name", out var nameElement))
                        {
                            continue;
                        }

                        var name = nameElement.GetString();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        if (detail.TryGetProperty("value", out var valueElement))
                        {
                            detailsMap[name] = ToPlainObject(valueElement);
                        }
                    }

                    return new FaceSummary(detailsMap);
                }
            }

            return new FaceSummary(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return new FaceSummary(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static object? ToPlainObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.TryGetDouble(out var doubleValue) ? doubleValue : null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ToPlainObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(prop => prop.Name, prop => ToPlainObject(prop.Value)),
            _ => null
        };
    }

    private static LivenessSummary ExtractLivenessSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = FindFirstString(root, "status") ?? FindFirstString(root, "liveness") ?? FindFirstString(root, "result");
            var score = FindFirstNumber(root, "score");
            return new LivenessSummary(status, score);
        }
        catch
        {
            return new LivenessSummary(null, null);
        }
    }

    private static FaceMatchSummary ExtractMatchSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                var first = results.EnumerateArray().FirstOrDefault();
                var similarity = FindFirstNumber(first, "similarity");
                var score = FindFirstNumber(first, "score");
                return new FaceMatchSummary(similarity, score);
            }
        }
        catch
        {
            // ignored
        }

        return new FaceMatchSummary(null, null);
    }

    private static double? FindFirstNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals(propertyName) && prop.Value.TryGetDouble(out var value))
                {
                    return value;
                }

                var nested = FindFirstNumber(prop.Value, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstNumber(item, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindFirstString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals(propertyName) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }

                var nested = FindFirstString(prop.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
