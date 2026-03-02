using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RegulaWebApp.Models;

namespace RegulaWebApp.Services;

public class DocumentFraudService : IDocumentFraudService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<DocROptions> _optionsAccessor;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public DocumentFraudService(
        IHttpClientFactory httpClientFactory,
        IOptions<DocROptions> optionsAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _optionsAccessor = optionsAccessor;
    }

    public async Task<IActionResult> DetectDocumentFraud(HttpRequest request)
    {
        var docRequest = await ReadDocumentRequestAsync(request);
        if (docRequest.Images.Count == 0)
        {
            return new BadRequestObjectResult(new
            {
                error = "Provide document images. Send multipart/form-data with 'images' or JSON with 'images' [{ base64, format }]."
            });
        }

        var payload = BuildProcessPayload(docRequest, forceAuth: true);
        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("DocR");

        using var response = await client.PostAsJsonAsync(options.ProcessEndpoint, payload);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return new ObjectResult(new { error = "DocR process request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var apiResponse = await response.Content.ReadFromJsonAsync<ProcessResponse>(JsonOptions);
        if (apiResponse is null)
        {
            return new ObjectResult(new { error = "Unable to read DocR response." })
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }

        var summary = BuildFraudSummary(apiResponse);
        return new OkObjectResult(summary)
        {
            StatusCode = (int)response.StatusCode
        };
    }

    private static DocRProcessRequest BuildProcessPayload(
        DocumentProcessRequest request,
        bool forceAuth = true)
    {
        var scenario = string.IsNullOrWhiteSpace(request.Scenario) ? "FullAuth" : request.Scenario;
        var processParam = new DocRProcessParam(
            scenario,
            forceAuth ? new DocRAuthParams(CheckLiveness: false) : null,
            null,
            null,
            null,
            null);

        var list = request.Images
            .Select(image => new DocRListItem(new DocRImageData(CleanBase64(image.Base64))))
            .ToList();

        return new DocRProcessRequest(processParam, list, null);
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

    private static FraudSummary BuildFraudSummary(ProcessResponse response)
    {
        try
        {
            var transactionId = response.TransactionInfo?.TransactionID;
            var overallStatus = FindOverallStatus(response);
            var checks = new List<FraudCheckResult>();
            var notApplicable = new List<string>();

            AddCheck(checks, notApplicable, EvaluateDocumentType(response));
            AddCheck(checks, notApplicable, EvaluateImageQuality(response));
            AddCheck(checks, notApplicable, EvaluateDocumentLiveness(response));
            AddCheck(checks, notApplicable, EvaluateHologramOvi(response));
            AddCheck(checks, notApplicable, EvaluateMrz(response));
            AddCheck(checks, notApplicable, EvaluateVisualOcr(response));
            AddCheck(checks, notApplicable, EvaluatePhotoEmbedding(response));
            AddCheck(checks, notApplicable, EvaluateSecurityPattern(response));
            AddCheck(checks, notApplicable, EvaluateExtendedMrzOcr(response));
            AddCheck(checks, notApplicable, EvaluateGeometry(response));
            AddCheck(checks, notApplicable, EvaluateDataCrossValidation(response));

            return new FraudSummary
            {
                TransactionId = transactionId,
                OverallStatus = overallStatus,
                Checks = checks,
                NotApplicable = notApplicable
            };
        }
        catch
        {
            return new FraudSummary
            {
                Checks = new List<FraudCheckResult>
                {
                    new FraudCheckResult
                    {
                        Name = "Parsing",
                        Status = "unknown",
                        Details = "Unable to parse DocR response.",
                        EvidencePaths = new List<string>()
                    }
                },
                NotApplicable = new List<string>()
            };
        }
    }

    private static void AddCheck(List<FraudCheckResult> checks, List<string> notApplicable, FraudCheckResult result)
    {
        checks.Add(result);
        if (string.Equals(result.Status, "not_applicable", StringComparison.OrdinalIgnoreCase))
        {
            notApplicable.Add(result.Name);
        }
    }

    private static FraudCheckResult EvaluateDocumentType(ProcessResponse response)
    {
        var candidate = GetContainers(response)
            .Select(item => item.OneCandidate)
            .FirstOrDefault(item => item is not null);

        if (candidate is not null)
        {
            var name = candidate.DocumentName;
            var fdsCount = candidate.FDSIDList?.Count;
            var icao = candidate.FDSIDList?.ICAOCode;

            var hasTemplate = !string.IsNullOrWhiteSpace(name) || (fdsCount.HasValue && fdsCount.Value > 0);
            var status = hasTemplate ? "pass" : "fail";
            var details = hasTemplate
                ? $"Matched template: {name ?? "unknown"} (ICAO {icao ?? "n/a"})."
                : "No document template match in the response.";

            return new FraudCheckResult
            {
                Name = "Document Type Identification",
                Status = status,
                Details = details,
                EvidencePaths = new List<string> { "ContainerList.List[].OneCandidate" }
            };
        }

        var docType = GetContainers(response)
            .SelectMany(item => item.DocType ?? new List<DocumentType>())
            .FirstOrDefault();

        if (docType is not null)
        {
            var details = $"Matched template: {docType.Name ?? "unknown"} (ICAO {docType.ICAOCode ?? "n/a"}).";
            return new FraudCheckResult
            {
                Name = "Document Type Identification",
                Status = "pass",
                Details = details,
                EvidencePaths = new List<string> { "ContainerList.List[].DocType" }
            };
        }

        return NotApplicable("Document Type Identification", "No OneCandidate or DocType section in response.");
    }

    private static FraudCheckResult EvaluateImageQuality(ProcessResponse response)
    {
        var qualityList = GetContainers(response)
            .Select(item => item.ImageQualityCheckList)
            .FirstOrDefault(item => item?.List.Count > 0);

        if (qualityList is null)
        {
            return NotApplicable("Image Quality Assessment", "No ImageQualityCheckList section in response.");
        }

        var failed = new List<string>();
        var passed = 0;

        foreach (var item in qualityList.List)
        {
            var result = item.Result;
            var type = item.Type;
            var probability = item.Probability;

            if (result.HasValue && result.Value == 0)
            {
                failed.Add($"type={type?.ToString() ?? "?"}, probability={probability?.ToString() ?? "?"}");
            }
            else if (result.HasValue && result.Value == 1)
            {
                passed++;
            }
        }

        if (failed.Count > 0)
        {
            return new FraudCheckResult
            {
                Name = "Image Quality Assessment",
                Status = "fail",
                Details = $"Quality checks failed ({failed.Count}). Examples: {string.Join("; ", failed.Take(3))}.",
                EvidencePaths = new List<string> { "ContainerList.List[].ImageQualityCheckList.List" }
            };
        }

        return new FraudCheckResult
        {
            Name = "Image Quality Assessment",
            Status = passed > 0 ? "pass" : "unknown",
            Details = passed > 0
                ? "All reported quality checks passed."
                : "Quality checks present but status could not be confirmed.",
            EvidencePaths = new List<string> { "ContainerList.List[].ImageQualityCheckList" }
        };
    }
    private static FraudCheckResult EvaluateDocumentLiveness(ProcessResponse response)
    {
        var integrity = GetContainers(response)
            .Select(item => item.Status?.CaptureProcessIntegrity)
            .FirstOrDefault(value => value.HasValue);

        if (!integrity.HasValue)
        {
            return NotApplicable("Document Liveness Check", "captureProcessIntegrity not provided.");
        }

        var status = integrity.Value switch
        {
            1 => "pass",
            0 => "fail",
            _ => "unknown"
        };

        var details = $"captureProcessIntegrity={integrity.Value}.";
        return new FraudCheckResult
        {
            Name = "Document Liveness Check",
            Status = status,
            Details = details,
            EvidencePaths = new List<string> { "ContainerList.List[].Status.captureProcessIntegrity" }
        };
    }

    private static FraudCheckResult EvaluateHologramOvi(ProcessResponse response)
    {
        var elements = ExtractAuthenticityElements(response);
        if (elements.Count == 0)
        {
            return NotApplicable("Hologram / OVI / MLI / Dynaprint Check", "No Authenticity elements in response.");
        }

        var failed = elements.Where(e => e.Result == 0).ToList();
        var passed = elements.Where(e => e.Result == 1).ToList();

        if (failed.Count > 0)
        {
            return new FraudCheckResult
            {
                Name = "Hologram / OVI / MLI / Dynaprint Check",
                Status = "fail",
                Details = $"Authenticity elements failed: {string.Join("; ", failed.Take(3).Select(FormatAuthenticityElement))}.",
                EvidencePaths = new List<string> { "ContainerList.List[].AuthenticityCheckList/Authenticity.CheckList" }
            };
        }

        return new FraudCheckResult
        {
            Name = "Hologram / OVI / MLI / Dynaprint Check",
            Status = passed.Count > 0 ? "pass" : "unknown",
            Details = passed.Count > 0
                ? $"Authenticity elements passed ({passed.Count})."
                : "Authenticity elements present but status could not be determined.",
            EvidencePaths = new List<string> { "ContainerList.List[].AuthenticityCheckList/Authenticity.CheckList" }
        };
    }

    private static FraudCheckResult EvaluateMrz(ProcessResponse response)
    {
        var mrzQuality = GetContainers(response)
            .Select(item => item.MRZTestQuality)
            .FirstOrDefault(item => item is not null);

        var mrzText = FindTextField(response, "MRZ Strings");

        if (mrzQuality is null && mrzText is null)
        {
            return NotApplicable("MRZ (Machine Readable Zone) Check", "No MRZTestQuality or MRZ Strings data.");
        }

        var checksum = mrzQuality?.CheckSums;
        var mrzValid = mrzText?.ValidityStatus;

        var failedReasons = new List<string>();
        if (checksum.HasValue && checksum.Value == 0)
        {
            failedReasons.Add("MRZ checksum failed");
        }
        if (mrzValid.HasValue && mrzValid.Value == 0)
        {
            failedReasons.Add("MRZ text validity failed");
        }

        if (failedReasons.Count > 0)
        {
            return new FraudCheckResult
            {
                Name = "MRZ (Machine Readable Zone) Check",
                Status = "fail",
                Details = string.Join("; ", failedReasons),
                EvidencePaths = new List<string> { "ContainerList.List[].MRZTestQuality", "ContainerList.List[].Text.fieldList[MRZ Strings]" }
            };
        }

        return new FraudCheckResult
        {
            Name = "MRZ (Machine Readable Zone) Check",
            Status = "pass",
            Details = "MRZ checksum and MRZ text validity passed.",
            EvidencePaths = new List<string> { "ContainerList.List[].MRZTestQuality", "ContainerList.List[].Text.fieldList[MRZ Strings]" }
        };
    }

    private static FraudCheckResult EvaluateVisualOcr(ProcessResponse response)
    {
        var visualFields = new List<(string name, int? validity)>();
        foreach (var text in GetContainers(response).Select(item => item.Text).Where(item => item is not null))
        {
            foreach (var field in text!.FieldList)
            {
                var name = field.FieldName ?? "Field";
                var validity = field.ValidityStatus ?? field.Status;
                var hasVisual = FieldHasVisualSource(field);
                if (hasVisual)
                {
                    visualFields.Add((name, validity));
                }
            }
        }

        if (visualFields.Count == 0)
        {
            return NotApplicable("Visual Zone OCR Validation", "No visual OCR fields found.");
        }

        var failed = visualFields.Where(item => item.validity.HasValue && item.validity.Value == 0).ToList();
        if (failed.Count > 0)
        {
            var sample = string.Join(", ", failed.Take(5).Select(item => item.name));
            return new FraudCheckResult
            {
                Name = "Visual Zone OCR Validation",
                Status = "fail",
                Details = $"Visual OCR validity failed for {failed.Count} field(s): {sample}.",
                EvidencePaths = new List<string> { "ContainerList.List[].Text.fieldList" }
            };
        }

        return new FraudCheckResult
        {
            Name = "Visual Zone OCR Validation",
            Status = "pass",
            Details = "Visual OCR fields validated successfully.",
            EvidencePaths = new List<string> { "ContainerList.List[].Text.fieldList" }
        };
    }

    private static FraudCheckResult EvaluatePhotoEmbedding(ProcessResponse response)
    {
        var hasImages = GetContainers(response).Any(item => item.Images?.FieldList.Count > 0);
        var hasGraphics = GetContainers(response).Any(item => item.DocGraphicsInfo?.PArrayFields.Count > 0);

        if (!hasImages && !hasGraphics)
        {
            return NotApplicable("Photo Embedding Check", "No Images or DocGraphicsInfo sections in response.");
        }

        var hasPortrait = GetContainers(response).Any(item => HasImageField(item.Images, "Portrait")) ||
                          GetContainers(response).Any(item => HasGraphicsField(item.DocGraphicsInfo, "Portrait"));
        var hasGhost = GetContainers(response).Any(item => HasImageField(item.Images, "Ghost portrait"));

        var status = hasPortrait ? "pass" : "fail";
        var details = hasPortrait
            ? $"Portrait image present. Ghost portrait present: {hasGhost}."
            : "No portrait image found in visual graphics.";

        return new FraudCheckResult
        {
            Name = "Photo Embedding Check",
            Status = status,
            Details = details,
            EvidencePaths = new List<string> { "ContainerList.List[].Images.fieldList", "ContainerList.List[].DocGraphicsInfo.pArrayFields" }
        };
    }

    private static FraudCheckResult EvaluateSecurityPattern(ProcessResponse response)
    {
        var security = GetContainers(response)
            .Select(item => item.Status?.DetailsOptical?.Security)
            .FirstOrDefault(value => value.HasValue);

        if (!security.HasValue)
        {
            return NotApplicable("Security Pattern / Image Pattern Check", "No security status in detailsOptical.");
        }

        var status = security.Value switch
        {
            1 => "pass",
            0 => "fail",
            _ => "unknown"
        };

        return new FraudCheckResult
        {
            Name = "Security Pattern / Image Pattern Check",
            Status = status,
            Details = $"detailsOptical.security={security.Value}.",
            EvidencePaths = new List<string> { "ContainerList.List[].Status.detailsOptical.security" }
        };
    }
    
    private static FraudCheckResult EvaluateExtendedMrzOcr(ProcessResponse response)
    {
        var textNode = GetContainers(response)
            .Select(item => item.Text)
            .FirstOrDefault(item => item is not null && item.FieldList.Count > 0);

        if (textNode is null)
        {
            return NotApplicable("Extended MRZ & Extended OCR", "No Text section in response.");
        }

        var comparisonStatus = textNode.ComparisonStatus;
        var mismatches = new List<string>();
        foreach (var field in textNode.FieldList)
        {
            var status = field.ComparisonStatus;
            if (status.HasValue && status.Value != 1)
            {
                mismatches.Add(field.FieldName ?? "Field");
            }
        }

        if (mismatches.Count > 0)
        {
            var sample = string.Join(", ", mismatches.Take(5));
            return new FraudCheckResult
            {
                Name = "Extended MRZ & Extended OCR",
                Status = "fail",
                Details = $"Field comparison mismatches detected: {sample}.",
                EvidencePaths = new List<string> { "ContainerList.List[].Text.fieldList" }
            };
        }

        var statusLabel = comparisonStatus.HasValue && comparisonStatus.Value == 1 ? "pass" : "unknown";
        return new FraudCheckResult
        {
            Name = "Extended MRZ & Extended OCR",
            Status = statusLabel,
            Details = statusLabel == "pass"
                ? "MRZ/OCR extended field comparisons passed."
                : "No mismatches found, but comparison status is not confirmed.",
            EvidencePaths = new List<string> { "ContainerList.List[].Text.comparisonStatus" }
        };
    }

    private static FraudCheckResult EvaluateGeometry(ProcessResponse response)
    {
        var position = GetContainers(response)
            .Select(item => item.DocumentPosition)
            .FirstOrDefault(item => item is not null);

        if (position is null)
        {
            return NotApplicable("Geometry Check", "No DocumentPosition section in response.");
        }

        var resultStatus = position.ResultStatus;
        var angle = position.Angle;
        var perspective = position.PerspectiveTr;

        var status = resultStatus switch
        {
            1 => "pass",
            0 => "fail",
            _ => "unknown"
        };

        return new FraudCheckResult
        {
            Name = "Geometry Check",
            Status = status,
            Details = $"DocumentPosition.ResultStatus={resultStatus?.ToString() ?? "n/a"}, Angle={angle?.ToString() ?? "n/a"}, PerspectiveTr={perspective?.ToString() ?? "n/a"}.",
            EvidencePaths = new List<string> { "ContainerList.List[].DocumentPosition" }
        };
    }

    private static FraudCheckResult EvaluateDataCrossValidation(ProcessResponse response)
    {
        var textNode = GetContainers(response)
            .Select(item => item.Text)
            .FirstOrDefault(item => item is not null && item.FieldList.Count > 0);

        if (textNode is null)
        {
            return NotApplicable("Data Cross-Validation", "No Text section in response.");
        }

        var comparisonStatus = textNode.ComparisonStatus;
        var mismatches = new List<string>();
        foreach (var field in textNode.FieldList)
        {
            var status = field.ComparisonStatus;
            if (status.HasValue && status.Value != 1)
            {
                mismatches.Add(field.FieldName ?? "Field");
            }
        }

        if (mismatches.Count > 0)
        {
            return new FraudCheckResult
            {
                Name = "Data Cross-Validation",
                Status = "fail",
                Details = $"Data mismatches detected across sources: {string.Join(", ", mismatches.Take(5))}.",
                EvidencePaths = new List<string> { "ContainerList.List[].Text.fieldList" }
            };
        }

        var statusLabel = comparisonStatus.HasValue && comparisonStatus.Value == 1 ? "pass" : "unknown";
        return new FraudCheckResult
        {
            Name = "Data Cross-Validation",
            Status = statusLabel,
            Details = statusLabel == "pass"
                ? "MRZ/visual comparisons are consistent."
                : "No mismatches found, but comparison status is not confirmed.",
            EvidencePaths = new List<string> { "ContainerList.List[].Text.comparisonStatus" }
        };
    }

    private static FraudCheckResult NotApplicable(string name, string details)
    {
        return new FraudCheckResult
        {
            Name = name,
            Status = "not_applicable",
            Details = details,
            EvidencePaths = new List<string>()
        };
    }

    private static string? FindOverallStatus(ProcessResponse response)
    {
        var overall = GetContainers(response)
            .Select(item => item.Status?.OverallStatus)
            .FirstOrDefault(value => value.HasValue);

        return overall?.ToString();
    }

    private static bool FieldHasVisualSource(TextField field)
    {
        return field.ValueList.Any(value => value.SourceType == SourceType.Visual);
    }

    private static bool HasImageField(ImagesResult? images, string fieldName)
    {
        if (images is null)
        {
            return false;
        }

        return images.FieldList.Any(field =>
            string.Equals(field.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasGraphicsField(DocGraphicsInfo? graphics, string fieldName)
    {
        if (graphics is null)
        {
            return false;
        }

        return graphics.PArrayFields.Any(field =>
            string.Equals(field.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static TextField? FindTextField(ProcessResponse response, string fieldName)
    {
        foreach (var text in GetContainers(response).Select(item => item.Text).Where(item => item is not null))
        {
            var field = text!.FieldList.FirstOrDefault(item =>
                string.Equals(item.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static List<AuthenticityElementInfo> ExtractAuthenticityElements(ProcessResponse response)
    {
        var results = new List<AuthenticityElementInfo>();

        foreach (var item in GetContainers(response))
        {
            var authList = item.AuthenticityCheckList?.List ?? new List<AuthenticityCheckGroup>();
            foreach (var group in authList)
            {
                foreach (var element in group.List)
                {
                    results.Add(new AuthenticityElementInfo(
                        element.Type,
                        element.ElementType,
                        element.ElementDiagnose,
                        element.ElementResult));
                }
            }

            var auth = item.Authenticity;
            if (auth is not null)
            {
                foreach (var check in auth.CheckList)
                {
                    foreach (var element in check.ElementList)
                    {
                        var result = element.ElementResult;
                        if (!result.HasValue)
                        {
                            result = element.Status switch
                            {
                                CheckResult.OK => 1,
                                CheckResult.Failed => 0,
                                _ => null
                            };
                        }

                        results.Add(new AuthenticityElementInfo(
                            check.Type,
                            element.ElementType,
                            element.ElementDiagnose,
                            result));
                    }
                }
            }
        }

        return results;
    }

    private static string FormatAuthenticityElement(AuthenticityElementInfo element)
    {
        return $"Type={element.Type?.ToString() ?? "?"}, Element={element.ElementType?.ToString() ?? "?"}, Diagnose={element.Diagnose?.ToString() ?? "?"}, Result={element.Result?.ToString() ?? "?"}";
    }

    private static IEnumerable<ContainerListItem> GetContainers(ProcessResponse response)
    {
        return response.ContainerList?.List ?? Enumerable.Empty<ContainerListItem>();
    }

    private sealed record AuthenticityElementInfo(int? Type, int? ElementType, int? Diagnose, int? Result);
}
