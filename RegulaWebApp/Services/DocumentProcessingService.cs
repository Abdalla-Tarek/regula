using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RegulaWebApp.Models;

namespace RegulaWebApp.Services;

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<DocROptions> _optionsAccessor;
    private readonly IRegulaService _regulaService;

    public DocumentProcessingService(
        IHttpClientFactory httpClientFactory,
        IOptions<DocROptions> optionsAccessor,
        IRegulaService regulaService)
    {
        _httpClientFactory = httpClientFactory;
        _optionsAccessor = optionsAccessor;
        _regulaService = regulaService;
    }

    public async Task<IActionResult> ProcessDocument(HttpRequest request)
    {
        var docRequest = await ReadDocumentRequestAsync(request);
        if (docRequest.Images.Count == 0)
        {
            return new BadRequestObjectResult(new { error = "Provide document images. Send multipart/form-data with 'images' or JSON with 'images' [{ base64, format }]." });
        }

        var payload = BuildProcessPayload(docRequest);
        return await SendToDocRAsync(payload);
    }

    public async Task<IActionResult> FraudDetection(HttpRequest request)
    {
        var docRequest = await ReadDocumentRequestAsync(request);
        if (docRequest.Images.Count == 0)
        {
            return new BadRequestObjectResult(new { error = "Provide document images. Send multipart/form-data with 'images' or JSON with 'images' [{ base64, format }]." });
        }

        var payload = BuildProcessPayload(docRequest, forceAuth: true);
        return await SendToDocRAsync(payload);
    }

    public async Task<IActionResult> VerifyIdentity(HttpRequest request)
    {
        var docRequest = await ReadDocumentRequestAsync(request);
        if (docRequest.Images.Count == 0)
        {
            return new BadRequestObjectResult(new { error = "Provide document images. Send multipart/form-data with 'images'." });
        }

        if (string.IsNullOrWhiteSpace(docRequest.LivePortraitBase64))
        {
            return new BadRequestObjectResult(new { error = "Provide a live portrait. Send form field 'livePortrait' with base64 or JSON 'livePortraitBase64'." });
        }

        var payload = BuildProcessPayload(docRequest, forceAuth: false);
        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("DocR");

        using var response = await client.PostAsJsonAsync(options.ProcessEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "DocR process request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var documentPortrait = ExtractDocumentPortraitBase64(content);
        if (string.IsNullOrWhiteSpace(documentPortrait))
        {
            return new ObjectResult(new { error = "Unable to extract document portrait from DocR response." })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        var livePortrait = CleanBase64(docRequest.LivePortraitBase64 ?? string.Empty);
        var matchResult = await _regulaService.MatchFaces(
            CleanBase64(documentPortrait),
            livePortrait);

        if (!string.IsNullOrWhiteSpace(matchResult.error))
        {
            return new ObjectResult(new { error = matchResult.error, details = matchResult.details })
            {
                StatusCode = matchResult.statusCode ?? StatusCodes.Status502BadGateway
            };
        }

        var similarityPercent = NormalizeSimilarityPercent(matchResult.similarity);
        return new OkObjectResult(new { similarityPercent });
    }

    private DocRProcessRequest BuildProcessPayload(
        DocumentProcessRequest request,
        bool forceAuth = true,
        bool includeLivePortrait = false,
        bool useFaceApi = false,
        DocRFaceApiConfig? faceApiConfig = null,
        bool? checkLiveness = null,
        bool? oneShotIdentification = null)
    {
        var scenario = string.IsNullOrWhiteSpace(request.Scenario) ? "FullAuth" : request.Scenario;
        var processParam = new DocRProcessParam(
            scenario,
            forceAuth ? new DocRAuthParams(CheckLiveness: false) : null,
            useFaceApi ? true : null,
            useFaceApi ? faceApiConfig : null,
            checkLiveness,
            oneShotIdentification
        );

        var list = request.Images
            .Select(image => new DocRListItem(new DocRImageData(CleanBase64(image.Base64))))
            .ToList();

        var livePortrait = includeLivePortrait
            ? CleanBase64(request.LivePortraitBase64 ?? string.Empty)
            : null;

        return new DocRProcessRequest(processParam, list, livePortrait);
    }

    private async Task<IActionResult> SendToDocRAsync(object payload)
    {
        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("DocR");

        using var response = await client.PostAsJsonAsync(options.ProcessEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "DocR process request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var summary = BuildDocRSummary(content);
        return new JsonResult(summary)
        {
            StatusCode = (int)response.StatusCode
        };
    }

    private static async Task<DocumentProcessRequest> ReadDocumentRequestAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var files = form.Files;
            var images = new List<DocumentImage>();
            foreach (var file in files)
            {
                var base64 = await ReadFileAsBase64Async(file);
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    images.Add(new DocumentImage(base64, GetFormat(file)));
                }
            }

            return new DocumentProcessRequest
            {
                Images = images,
                Scenario = form.TryGetValue("scenario", out var scenario) ? scenario.ToString() : null,
                Tag = form.TryGetValue("tag", out var tag) ? tag.ToString() : null,
                LivePortraitBase64 = form.TryGetValue("livePortrait", out var livePortrait)
                    ? livePortrait.ToString()
                    : null
            };
        }

        return await request.ReadFromJsonAsync<DocumentProcessRequest>() ?? new DocumentProcessRequest();
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

    private static string CleanBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = value.Replace("data:image/jpeg;base64,", "").Replace("data:image/png;base64,", "");

        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0 && value[..commaIndex].Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return value[(commaIndex + 1)..];
        }

        return value;
    }

    private static object BuildDocRSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var transactionId = FindFirstString(root, new[] { "transactionId", "transactionID", "id", "TransactionId", "TransactionID" });

            var fields = ExtractDocVisualFields(root);
            var surname = GetFieldValue(fields, "Surname", "Last Name", "Family Name");
            var givenNames = GetFieldValue(fields, "Given Names", "Given Name", "First Name");
            var fullName = !string.IsNullOrWhiteSpace(surname) && !string.IsNullOrWhiteSpace(givenNames)
                ? $"{surname} {givenNames}".Trim()
                : GetFieldValue(fields, "Name", "Full Name");
            var documentNumber = GetFieldValue(fields, "Document Number", "Document No.", "Doc Number", "DocumentNo", "Document ID");
            var documentType = GetFieldValue(fields, "Document Class Code", "Document Type", "Document Type Code", "Document Class");
            var dateOfBirth = GetFieldValue(fields, "Date of Birth", "Birth Date", "DOB");
            var expiryDate = GetFieldValue(fields, "Date of Expiry", "Date of Expiration", "Expiry Date", "Expiration Date");

            var validity = ExtractValiditySummary(root);
            var overallStatus = validity.valid.Count > 0 || validity.invalid.Count > 0
                ? (validity.invalid.Count == 0 ? "valid" : "invalid")
                : FindFirstString(root, new[] { "status", "overallStatus", "result", "ResultStatus" });

            return new
            {
                transactionId,
                overallStatus,
                documentType,
                documentNumber,
                fullName,
                dateOfBirth,
                expiryDate,
                validity = new
                {
                    valid = validity.valid,
                    invalid = validity.invalid
                }
            };
        }
        catch
        {
            return new { error = "Unable to parse DocR response." };
        }
    }

    private static (List<string> valid, List<string> invalid) ExtractValiditySummary(JsonElement root)
    {
        var valid = new List<string>();
        var invalid = new List<string>();
        CollectAuthenticityValidity(root, valid, invalid);
        return (
            valid: valid.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            invalid: invalid.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    private static void CollectAuthenticityValidity(JsonElement element, List<string> valid, List<string> invalid)
    {
        if (!TryGetContainerList(element, out var containerList))
        {
            return;
        }

        foreach (var item in containerList.EnumerateArray())
        {
            if (!item.TryGetProperty("AuthenticityCheckList", out var authList) ||
                authList.ValueKind != JsonValueKind.Object ||
                !authList.TryGetProperty("List", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var group in list.EnumerateArray())
            {
                if (!group.TryGetProperty("List", out var checks) || checks.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var check in checks.EnumerateArray())
                {
                    var label = BuildAuthenticityLabel(check);
                    var isValid = ReadFirstBool(check, "ElementResult", "Result", "ElementDiagnose");
                    if (isValid is null || string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    if (isValid.Value)
                    {
                        valid.Add(label);
                    }
                    else
                    {
                        invalid.Add(label);
                    }
                }
            }
        }
    }

    private static bool TryGetContainerList(JsonElement root, out JsonElement list)
    {
        list = default;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("ContainerList", out var container) &&
            container.ValueKind == JsonValueKind.Object &&
            container.TryGetProperty("List", out list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> ExtractDocVisualFields(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetContainerList(root, out var list))
        {
            return result;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (!item.TryGetProperty("DocVisualExtendedInfo", out var docInfo) ||
                docInfo.ValueKind != JsonValueKind.Object ||
                !docInfo.TryGetProperty("pArrayFields", out var fields) ||
                fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var field in fields.EnumerateArray())
            {
                if (!field.TryGetProperty("FieldName", out var nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var value = field.TryGetProperty("Buf_Text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[name] = value.Trim();
                }
            }
        }

        return result;
    }

    private static string? GetFieldValue(Dictionary<string, string> fields, params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? ReadFirstBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                {
                    return value.GetBoolean();
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num))
                {
                    return num != 0;
                }
            }
        }

        return null;
    }

    private static string BuildAuthenticityLabel(JsonElement element)
    {
        var type = ReadFirstInt(element, "Type");
        var elementType = ReadFirstInt(element, "ElementType");
        var diagnose = ReadFirstInt(element, "ElementDiagnose");
        var parts = new List<string>();

        if (type.HasValue)
        {
            parts.Add($"Type {type.Value}");
        }

        if (elementType.HasValue)
        {
            parts.Add($"Element {elementType.Value}");
        }

        if (diagnose.HasValue)
        {
            parts.Add($"Diagnose {diagnose.Value}");
        }

        return parts.Count == 0 ? "AuthenticityCheck" : $"AuthenticityCheck ({string.Join(", ", parts)})";
    }

    private static int? ReadFirstInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static double? ExtractSimilarityPercent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var faceApiNode = FindFirstObjectByName(root, "faceApi") ?? FindFirstObjectByName(root, "FaceApi");
            if (!faceApiNode.HasValue)
            {
                return null;
            }

            var value = TryExtractSimilarityValue(faceApiNode.Value);
            if (!value.HasValue &&
                faceApiNode.Value.ValueKind == JsonValueKind.Object &&
                faceApiNode.Value.TryGetProperty("response", out var response) &&
                response.ValueKind == JsonValueKind.String)
            {
                value = TryExtractSimilarityFromJsonString(response.GetString());
            }

            return NormalizeSimilarityPercent(value);
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? FindFirstObjectByName(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.NameEquals(name) && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    return prop.Value;
                }

                var nested = FindFirstObjectByName(prop.Value, name);
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
                var nested = FindFirstObjectByName(item, name);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static double? TryExtractSimilarityValue(JsonElement element)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "similarity",
            "score",
            "matchScore",
            "match_score"
        };

        return FindFirstNumber(element, keys);
    }

    private static double? TryExtractSimilarityFromJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryExtractSimilarityValue(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static double? NormalizeSimilarityPercent(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var number = value.Value;
        if (number >= 0 && number <= 1)
        {
            return Math.Round(number * 100, 2);
        }

        if (number > 1 && number <= 100)
        {
            return Math.Round(number, 2);
        }

        return number;
    }

    private static double? FindFirstNumber(JsonElement element, HashSet<string> propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (propertyNames.Contains(prop.Name) && prop.Value.TryGetDouble(out var value))
                {
                    return value;
                }

                var nested = FindFirstNumber(prop.Value, propertyNames);
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
                var nested = FindFirstNumber(item, propertyNames);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindFirstString(JsonElement element, IEnumerable<string> propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (propertyNames.Any(name => prop.NameEquals(name)) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }

                var nested = FindFirstString(prop.Value, propertyNames);
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
                var nested = FindFirstString(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? GetFormat(IFormFile file)
    {
        var contentType = file.ContentType?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("pdf"))
            {
                return "pdf";
            }

            if (contentType.Contains("png"))
            {
                return "png";
            }

            if (contentType.Contains("jpeg") || contentType.Contains("jpg"))
            {
                return "jpg";
            }
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "pdf",
            ".png" => "png",
            ".jpg" => "jpg",
            ".jpeg" => "jpg",
            _ => null
        };
    }

    private static string? ExtractDocumentPortraitBase64(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var preferredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "portrait",
                "portraitimage",
                "portraitimagedata",
                "portraitimagebase64",
                "faceimage",
                "face",
                "image",
                "imagedata",
                "imagebase64"
            };

            var byKey = FindFirstBase64ByKeys(root, preferredKeys);
            if (!string.IsNullOrWhiteSpace(byKey))
            {
                return byKey;
            }

            return FindFirstBase64String(root);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFirstBase64ByKeys(JsonElement element, HashSet<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (keys.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (IsProbablyBase64(value))
                    {
                        return value;
                    }
                }

                var nested = FindFirstBase64ByKeys(prop.Value, keys);
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
                var nested = FindFirstBase64ByKeys(item, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindFirstBase64String(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (IsProbablyBase64(value))
                    {
                        return value;
                    }
                }

                var nested = FindFirstBase64String(prop.Value);
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
                var nested = FindFirstBase64String(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool IsProbablyBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 200 || trimmed.Length % 4 != 0)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            var isValid = (ch >= 'A' && ch <= 'Z') ||
                          (ch >= 'a' && ch <= 'z') ||
                          (ch >= '0' && ch <= '9') ||
                          ch is '+' or '/' or '=';
            if (!isValid)
            {
                return false;
            }
        }

        return true;
    }
}
