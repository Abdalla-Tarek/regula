using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RegulaWebApp.Models;

namespace RegulaWebApp.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    [HttpPost("process")]
    public async Task<IActionResult> ProcessDocument(
        IHttpClientFactory httpClientFactory,
        IOptions<DocROptions> optionsAccessor)
    {
        var request = await ReadDocumentRequestAsync(Request);
        if (request.Images.Count == 0)
        {
            return BadRequest(new { error = "Provide document images. Send multipart/form-data with 'images' or JSON with 'images' [{ base64, format }]." });
        }

        var payload = BuildProcessPayload(request);
        return await SendToDocRAsync(payload, httpClientFactory, optionsAccessor);
    }

    [HttpPost("fraud-detection")]
    public async Task<IActionResult> FraudDetection(
        IHttpClientFactory httpClientFactory,
        IOptions<DocROptions> optionsAccessor)
    {
        var request = await ReadDocumentRequestAsync(Request);
        if (request.Images.Count == 0)
        {
            return BadRequest(new { error = "Provide document images. Send multipart/form-data with 'images' or JSON with 'images' [{ base64, format }]." });
        }

        var payload = BuildProcessPayload(request, forceAuth: true);
        return await SendToDocRAsync(payload, httpClientFactory, optionsAccessor);
    }

    private static DocRProcessRequest BuildProcessPayload(DocumentProcessRequest request, bool forceAuth = true)
    {
        var scenario = string.IsNullOrWhiteSpace(request.Scenario) ? "FullAuth" : request.Scenario;
        var processParam = new DocRProcessParam(
            scenario,
            new DocRAuthParams(CheckLiveness: false)
        );

        var list = request.Images
            .Select(image => new DocRListItem(new DocRImageData(CleanBase64(image.Base64))))
            .ToList();

        return new DocRProcessRequest(processParam, list);
    }

    private static async Task<IActionResult> SendToDocRAsync(
        object payload,
        IHttpClientFactory httpClientFactory,
        IOptions<DocROptions> optionsAccessor)
    {
        var options = optionsAccessor.Value;
        var client = httpClientFactory.CreateClient("DocR");

        using var response = await client.PostAsJsonAsync(options.ProcessEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "DocR process request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        return new ContentResult
        {
            Content = content,
            ContentType = "application/json",
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
                Tag = form.TryGetValue("tag", out var tag) ? tag.ToString() : null
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

        var commaIndex = value.IndexOf(',');
        if (commaIndex >= 0 && value[..commaIndex].Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return value[(commaIndex + 1)..];
        }

        return value;
    }

    // BuildImageData removed; payload uses strongly-typed DocRImageData.

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
}
