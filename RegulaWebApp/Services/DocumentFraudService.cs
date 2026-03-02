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
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ObjectResult(new { error = "DocR process request failed.", details = content })
            {
                StatusCode = (int)response.StatusCode
            };
        }

        var summary = BuildFraudSummary(content);
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

    private static FraudSummary BuildFraudSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var transactionId = FindFirstString(root, new[]
            {
                "transactionId", "transactionID", "id", "TransactionId", "TransactionID"
            });

            var overallStatus = FindOverallStatus(root);
            var checks = new List<FraudCheckResult>();
            var notApplicable = new List<string>();

            AddCheck(checks, notApplicable, EvaluateDocumentType(root));
            AddCheck(checks, notApplicable, EvaluateImageQuality(root));
            AddCheck(checks, notApplicable, EvaluateDocumentLiveness(root));
            AddCheck(checks, notApplicable, EvaluateHologramOvi(root));
            AddCheck(checks, notApplicable, EvaluateMrz(root));
            AddCheck(checks, notApplicable, EvaluateBarcode(root));
            AddCheck(checks, notApplicable, EvaluateVisualOcr(root));
            AddCheck(checks, notApplicable, EvaluatePhotoEmbedding(root));
            AddCheck(checks, notApplicable, EvaluatePortraitCrossCheck(root));
            AddCheck(checks, notApplicable, EvaluateSecurityPattern(root));
            AddCheck(checks, notApplicable, EvaluateIpi(root));
            AddCheck(checks, notApplicable, EvaluateEncryptedIpi(root));
            AddCheck(checks, notApplicable, EvaluateUvIr(root));
            AddCheck(checks, notApplicable, EvaluateExtendedMrzOcr(root));
            AddCheck(checks, notApplicable, EvaluateAxialProtection(root));
            AddCheck(checks, notApplicable, EvaluateGeometry(root));
            AddCheck(checks, notApplicable, EvaluateDataCrossValidation(root));

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

    private static FraudCheckResult EvaluateDocumentType(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "OneCandidate");
        if (!node.HasValue)
        {
            return NotApplicable("Document Type Identification", "No OneCandidate section in response.");
        }

        var name = ReadString(node.Value, "DocumentName");
        var fdsList = ReadInt(node.Value, "FDSIDList", "Count");
        var icao = ReadString(node.Value, "FDSIDList", "ICAOCode");

        var hasTemplate = !string.IsNullOrWhiteSpace(name) || (fdsList.HasValue && fdsList.Value > 0);
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

    private static FraudCheckResult EvaluateImageQuality(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "ImageQualityCheckList");
        if (!node.HasValue)
        {
            return NotApplicable("Image Quality Assessment", "No ImageQualityCheckList section in response.");
        }

        var failed = new List<string>();
        var passed = 0;
        if (TryGetPropertyIgnoreCase(node.Value, "List", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                var result = ReadInt(item, "result");
                var type = ReadInt(item, "type");
                var probability = ReadInt(item, "probability");

                if (result.HasValue && result.Value == 0)
                {
                    failed.Add($"type={type?.ToString() ?? "?"}, probability={probability?.ToString() ?? "?"}");
                }
                else if (result.HasValue && result.Value == 1)
                {
                    passed++;
                }
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
    private static FraudCheckResult EvaluateDocumentLiveness(JsonElement root)
    {
        var statusNode = FindFirstObjectByNameIgnoreCase(root, "Status");
        if (!statusNode.HasValue)
        {
            return NotApplicable("Document Liveness Check", "No Status section in response.");
        }

        var integrity = ReadInt(statusNode.Value, "captureProcessIntegrity");
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

    private static FraudCheckResult EvaluateHologramOvi(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "AuthenticityCheckList");
        if (!node.HasValue)
        {
            return NotApplicable("Hologram / OVI / MLI / Dynaprint Check", "No AuthenticityCheckList section in response.");
        }

        var elements = ExtractAuthenticityElements(node.Value);
        if (elements.Count == 0)
        {
            return new FraudCheckResult
            {
                Name = "Hologram / OVI / MLI / Dynaprint Check",
                Status = "unknown",
                Details = "Authenticity check list present but no elements found.",
                EvidencePaths = new List<string> { "ContainerList.List[].AuthenticityCheckList" }
            };
        }

        var failed = elements.Where(e => e.ElementResult == 0).ToList();
        var passed = elements.Where(e => e.ElementResult == 1).ToList();

        if (failed.Count > 0)
        {
            return new FraudCheckResult
            {
                Name = "Hologram / OVI / MLI / Dynaprint Check",
                Status = "fail",
                Details = $"Authenticity elements failed: {string.Join("; ", failed.Take(3).Select(FormatAuthenticityElement))}.",
                EvidencePaths = new List<string> { "ContainerList.List[].AuthenticityCheckList.List[].List[]" }
            };
        }

        return new FraudCheckResult
        {
            Name = "Hologram / OVI / MLI / Dynaprint Check",
            Status = passed.Count > 0 ? "pass" : "unknown",
            Details = passed.Count > 0
                ? $"Authenticity elements passed ({passed.Count})."
                : "Authenticity elements present but status could not be determined.",
            EvidencePaths = new List<string> { "ContainerList.List[].AuthenticityCheckList.List[].List[]" }
        };
    }

    private static FraudCheckResult EvaluateMrz(JsonElement root)
    {
        var mrzQuality = FindFirstObjectByNameIgnoreCase(root, "MRZTestQuality");
        var mrzText = FindTextField(root, "MRZ Strings");

        if (!mrzQuality.HasValue && mrzText is null)
        {
            return NotApplicable("MRZ (Machine Readable Zone) Check", "No MRZTestQuality or MRZ Strings data.");
        }

        var checksum = mrzQuality.HasValue ? ReadInt(mrzQuality.Value, "CHECK_SUMS") : null;
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

    private static FraudCheckResult EvaluateBarcode(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "DocBarCodeInfo");
        if (!node.HasValue)
        {
            return NotApplicable("Barcode & QR Code Check", "No DocBarCodeInfo section in response.");
        }

        var codeResult = ReadInt(node.Value, "pArrayFields", "0", "bcCodeResult");
        var decodeType = ReadInt(node.Value, "pArrayFields", "0", "bcType_DECODE");
        var status = codeResult.HasValue && codeResult.Value > 0 ? "pass" : "fail";

        var details = status == "pass"
            ? $"Barcode decoded (bcCodeResult={codeResult}, type={decodeType})."
            : "Barcode present but decoding failed or returned no data.";

        return new FraudCheckResult
        {
            Name = "Barcode & QR Code Check",
            Status = status,
            Details = details,
            EvidencePaths = new List<string> { "ContainerList.List[].DocBarCodeInfo" }
        };
    }
    private static FraudCheckResult EvaluateVisualOcr(JsonElement root)
    {
        var textNode = FindFirstObjectByNameIgnoreCase(root, "Text");
        if (!textNode.HasValue)
        {
            return NotApplicable("Visual Zone OCR Validation", "No Text section in response.");
        }

        if (!TryGetPropertyIgnoreCase(textNode.Value, "fieldList", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return NotApplicable("Visual Zone OCR Validation", "No fieldList in Text section.");
        }

        var visualFields = new List<(string name, int? validity)>();
        foreach (var field in fields.EnumerateArray())
        {
            var name = ReadString(field, "fieldName") ?? "Field";
            var validity = ReadInt(field, "validityStatus");
            var hasVisual = FieldHasSource(field, "VISUAL");
            if (hasVisual)
            {
                visualFields.Add((name, validity));
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

    private static FraudCheckResult EvaluatePhotoEmbedding(JsonElement root)
    {
        var images = FindFirstObjectByNameIgnoreCase(root, "Images");
        var graphics = FindFirstObjectByNameIgnoreCase(root, "DocGraphicsInfo");

        if (!images.HasValue && !graphics.HasValue)
        {
            return NotApplicable("Photo Embedding Check", "No Images or DocGraphicsInfo sections in response.");
        }

        var hasPortrait = HasImageField(images, "Portrait") || HasGraphicsField(graphics, "Portrait");
        var hasGhost = HasImageField(images, "Ghost portrait");

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

    private static FraudCheckResult EvaluatePortraitCrossCheck(JsonElement root)
    {
        var hasFaceApi = FindFirstObjectByNameIgnoreCase(root, "faceApi").HasValue ||
                         FindFirstObjectByNameIgnoreCase(root, "FaceApi").HasValue;
        var hasRfid = FindFirstObjectByNameIgnoreCase(root, "RFID").HasValue ||
                      FindFirstObjectByNameIgnoreCase(root, "Rfid").HasValue;

        if (!hasFaceApi && !hasRfid)
        {
            return NotApplicable("Portrait / Face Cross-Check", "No face API or RFID portrait data in response.");
        }

        return new FraudCheckResult
        {
            Name = "Portrait / Face Cross-Check",
            Status = "unknown",
            Details = "Face comparison data present but mapping is not implemented yet.",
            EvidencePaths = new List<string> { "ContainerList.List[].faceApi", "ContainerList.List[].RFID" }
        };
    }

    private static FraudCheckResult EvaluateSecurityPattern(JsonElement root)
    {
        var statusNode = FindFirstObjectByNameIgnoreCase(root, "Status");
        if (!statusNode.HasValue)
        {
            return NotApplicable("Security Pattern / Image Pattern Check", "No Status section in response.");
        }

        var security = ReadInt(statusNode.Value, "detailsOptical", "security");
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
    private static FraudCheckResult EvaluateIpi(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "IPI");
        if (!node.HasValue)
        {
            return NotApplicable("IPI (Invisible Personal Information) Check", "No IPI data in response.");
        }

        return new FraudCheckResult
        {
            Name = "IPI (Invisible Personal Information) Check",
            Status = "unknown",
            Details = "IPI data present but parsing is not implemented.",
            EvidencePaths = new List<string> { "ContainerList.List[].IPI" }
        };
    }

    private static FraudCheckResult EvaluateEncryptedIpi(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "EncryptedIpi");
        if (!node.HasValue)
        {
            return NotApplicable("Encrypted IPI Check", "No Encrypted IPI data in response.");
        }

        return new FraudCheckResult
        {
            Name = "Encrypted IPI Check",
            Status = "unknown",
            Details = "Encrypted IPI data present but parsing is not implemented.",
            EvidencePaths = new List<string> { "ContainerList.List[].EncryptedIpi" }
        };
    }

    private static FraudCheckResult EvaluateUvIr(JsonElement root)
    {
        var uv = FindFirstObjectByNameIgnoreCase(root, "UV");
        var ir = FindFirstObjectByNameIgnoreCase(root, "IR");
        if (!uv.HasValue && !ir.HasValue)
        {
            return NotApplicable("UV / IR Security Checks", "No UV/IR data in response.");
        }

        return new FraudCheckResult
        {
            Name = "UV / IR Security Checks",
            Status = "unknown",
            Details = "UV/IR data present but parsing is not implemented.",
            EvidencePaths = new List<string> { "ContainerList.List[].UV", "ContainerList.List[].IR" }
        };
    }

    private static FraudCheckResult EvaluateExtendedMrzOcr(JsonElement root)
    {
        var textNode = FindFirstObjectByNameIgnoreCase(root, "Text");
        if (!textNode.HasValue)
        {
            return NotApplicable("Extended MRZ & Extended OCR", "No Text section in response.");
        }

        var comparisonStatus = ReadInt(textNode.Value, "comparisonStatus");
        if (!TryGetPropertyIgnoreCase(textNode.Value, "fieldList", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return NotApplicable("Extended MRZ & Extended OCR", "No fieldList in Text section.");
        }

        var mismatches = new List<string>();
        foreach (var field in fields.EnumerateArray())
        {
            var status = ReadInt(field, "comparisonStatus");
            if (status.HasValue && status.Value != 1)
            {
                var name = ReadString(field, "fieldName") ?? "Field";
                mismatches.Add(name);
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

    private static FraudCheckResult EvaluateAxialProtection(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "AxialProtection");
        if (!node.HasValue)
        {
            return NotApplicable("Axial Protection Check", "No axial protection data in response.");
        }

        return new FraudCheckResult
        {
            Name = "Axial Protection Check",
            Status = "unknown",
            Details = "Axial protection data present but parsing is not implemented.",
            EvidencePaths = new List<string> { "ContainerList.List[].AxialProtection" }
        };
    }

    private static FraudCheckResult EvaluateGeometry(JsonElement root)
    {
        var node = FindFirstObjectByNameIgnoreCase(root, "DocumentPosition");
        if (!node.HasValue)
        {
            return NotApplicable("Geometry Check", "No DocumentPosition section in response.");
        }

        var resultStatus = ReadInt(node.Value, "ResultStatus");
        var angle = ReadInt(node.Value, "Angle");
        var perspective = ReadInt(node.Value, "PerspectiveTr");

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

    private static FraudCheckResult EvaluateDataCrossValidation(JsonElement root)
    {
        var textNode = FindFirstObjectByNameIgnoreCase(root, "Text");
        if (!textNode.HasValue)
        {
            return NotApplicable("Data Cross-Validation", "No Text section in response.");
        }

        var comparisonStatus = ReadInt(textNode.Value, "comparisonStatus");
        if (!TryGetPropertyIgnoreCase(textNode.Value, "fieldList", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return NotApplicable("Data Cross-Validation", "No fieldList in Text section.");
        }

        var mismatches = new List<string>();
        foreach (var field in fields.EnumerateArray())
        {
            var status = ReadInt(field, "comparisonStatus");
            if (status.HasValue && status.Value != 1)
            {
                var name = ReadString(field, "fieldName") ?? "Field";
                mismatches.Add(name);
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

    private static string? FindOverallStatus(JsonElement root)
    {
        var statusNode = FindFirstObjectByNameIgnoreCase(root, "Status");
        if (!statusNode.HasValue)
        {
            return FindFirstString(root, new[] { "status", "overallStatus", "result", "ResultStatus" });
        }

        var overall = ReadInt(statusNode.Value, "overallStatus");
        if (overall.HasValue)
        {
            return overall.Value.ToString();
        }

        return FindFirstString(root, new[] { "status", "overallStatus", "result", "ResultStatus" });
    }

    private static bool FieldHasSource(JsonElement field, string source)
    {
        if (!TryGetPropertyIgnoreCase(field, "valueList", out var valueList) || valueList.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in valueList.EnumerateArray())
        {
            var itemSource = ReadString(item, "source");
            if (string.Equals(itemSource, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasImageField(JsonElement? imagesNode, string fieldName)
    {
        if (!imagesNode.HasValue)
        {
            return false;
        }

        if (!TryGetPropertyIgnoreCase(imagesNode.Value, "fieldList", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var field in fields.EnumerateArray())
        {
            var name = ReadString(field, "fieldName");
            if (string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGraphicsField(JsonElement? graphicsNode, string fieldName)
    {
        if (!graphicsNode.HasValue)
        {
            return false;
        }

        if (!TryGetPropertyIgnoreCase(graphicsNode.Value, "pArrayFields", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var field in fields.EnumerateArray())
        {
            var name = ReadString(field, "FieldName");
            if (string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static TextFieldInfo? FindTextField(JsonElement root, string fieldName)
    {
        var textNode = FindFirstObjectByNameIgnoreCase(root, "Text");
        if (!textNode.HasValue)
        {
            return null;
        }

        if (!TryGetPropertyIgnoreCase(textNode.Value, "fieldList", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var field in fields.EnumerateArray())
        {
            var name = ReadString(field, "fieldName");
            if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var validity = ReadInt(field, "validityStatus");
            return new TextFieldInfo(name ?? fieldName, validity);
        }

        return null;
    }

    private static List<AuthenticityElement> ExtractAuthenticityElements(JsonElement node)
    {
        var results = new List<AuthenticityElement>();
        if (!TryGetPropertyIgnoreCase(node, "List", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(group, "List", out var elements) || elements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in elements.EnumerateArray())
            {
                var type = ReadInt(element, "Type");
                var elementType = ReadInt(element, "ElementType");
                var diagnose = ReadInt(element, "ElementDiagnose");
                var result = ReadInt(element, "ElementResult");
                results.Add(new AuthenticityElement(type, elementType, diagnose, result));
            }
        }

        return results;
    }

    private static string FormatAuthenticityElement(AuthenticityElement element)
    {
        return $"Type={element.Type?.ToString() ?? "?"}, Element={element.ElementType?.ToString() ?? "?"}, Diagnose={element.Diagnose?.ToString() ?? "?"}, Result={element.ElementResult?.ToString() ?? "?"}";
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static JsonElement? FindFirstObjectByNameIgnoreCase(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.Object)
                {
                    return prop.Value;
                }

                var nested = FindFirstObjectByNameIgnoreCase(prop.Value, name);
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
                var nested = FindFirstObjectByNameIgnoreCase(item, name);
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

    private static string? ReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryGetPropertyOrIndex(current, segment, out var next))
            {
                return null;
            }
            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static int? ReadInt(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryGetPropertyOrIndex(current, segment, out var next))
            {
                return null;
            }
            current = next;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private static bool TryGetPropertyOrIndex(JsonElement element, string segment, out JsonElement value)
    {
        value = default;
        if (element.ValueKind == JsonValueKind.Object)
        {
            return TryGetPropertyIgnoreCase(element, segment, out value);
        }

        if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
        {
            var arr = element.EnumerateArray().ToList();
            if (index >= 0 && index < arr.Count)
            {
                value = arr[index];
                return true;
            }
        }

        return false;
    }

    private sealed record TextFieldInfo(string Name, int? ValidityStatus);
    private sealed record AuthenticityElement(int? Type, int? ElementType, int? Diagnose, int? ElementResult);
}
