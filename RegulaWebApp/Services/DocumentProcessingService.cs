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
        return new OkObjectResult(new
        {
            similarityPercent,
            documentPortraitBase64 = CleanBase64(documentPortrait)
        });
    }

    public async Task<IActionResult> ComparePassportsAsync(HttpRequest request)
    {
        var compareRequest = await request.ReadFromJsonAsync<IdentityDocumentCompareRequest>();
        if (compareRequest is null)
        {
            return new BadRequestObjectResult(new { error = "Provide JSON body with firstDocumentImageBase64 and secondDocumentImageBase64." });
        }

        var firstImage = CleanBase64(compareRequest.FirstDocumentImageBase64 ?? string.Empty);
        var secondImage = CleanBase64(compareRequest.SecondDocumentImageBase64 ?? string.Empty);

        if (string.IsNullOrWhiteSpace(firstImage) || string.IsNullOrWhiteSpace(secondImage))
        {
            return new BadRequestObjectResult(new { error = "Both document images are required." });
        }

        var firstResult = await ProcessDocumentImageAsync(firstImage);
        if (!string.IsNullOrWhiteSpace(firstResult.error))
        {
            return new ObjectResult(new { error = firstResult.error, details = firstResult.details })
            {
                StatusCode = firstResult.statusCode ?? StatusCodes.Status502BadGateway
            };
        }

        var secondResult = await ProcessDocumentImageAsync(secondImage);
        if (!string.IsNullOrWhiteSpace(secondResult.error))
        {
            return new ObjectResult(new { error = secondResult.error, details = secondResult.details })
            {
                StatusCode = secondResult.statusCode ?? StatusCodes.Status502BadGateway
            };
        }

        var firstDocument = firstResult.info;
        var secondDocument = secondResult.info;

        if (firstDocument is null || secondDocument is null)
        {
            return new ObjectResult(new { error = "Unable to parse document data from DocR responses." })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        if (string.IsNullOrWhiteSpace(firstDocument.PortraitImageBase64) ||
            string.IsNullOrWhiteSpace(secondDocument.PortraitImageBase64))
        {
            return new ObjectResult(new { error = "Unable to extract document portrait(s) for face matching." })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        var matchResult = await _regulaService.MatchFaces(
            CleanBase64(firstDocument.PortraitImageBase64),
            CleanBase64(secondDocument.PortraitImageBase64));

        if (!string.IsNullOrWhiteSpace(matchResult.error))
        {
            return new ObjectResult(new { error = matchResult.error, details = matchResult.details })
            {
                StatusCode = matchResult.statusCode ?? StatusCodes.Status502BadGateway
            };
        }

        var threshold = _optionsAccessor.Value.FaceApiThreshold ?? 0.85;
        var normalizedSimilarity = NormalizeSimilarityScore(matchResult.similarity);
        var faceMatchScorePercent = NormalizeSimilarityPercent(matchResult.similarity);
        var isFaceMatch = normalizedSimilarity.HasValue && normalizedSimilarity.Value >= threshold;

        var isDocumentNumberMatch = CompareNormalized(firstDocument.DocumentNumber, secondDocument.DocumentNumber);
        var isNameMatch = CompareNames(firstDocument, secondDocument);
        var isDobMatch = CompareDates(firstDocument.DateOfBirth, secondDocument.DateOfBirth);

        var result = new IdentityDocumentComparisonResult
        {
            FirstDocument = firstDocument,
            SecondDocument = secondDocument,
            FaceMatchScore = normalizedSimilarity,
            FaceMatchScorePercent = faceMatchScorePercent,
            IsFaceMatch = isFaceMatch,
            IsDocumentNumberMatch = isDocumentNumberMatch,
            IsNameMatch = isNameMatch,
            IsDobMatch = isDobMatch,
            FaceMatchThreshold = threshold
        };

        return new OkObjectResult(result);
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

            var documentPosition = ExtractDocumentPosition(root);

            return new DocRSummary
            {
                TransactionId = transactionId,
                OverallStatus = overallStatus,
                DocumentType = documentType,
                DocumentNumber = documentNumber,
                FullName = fullName,
                DateOfBirth = dateOfBirth,
                ExpiryDate = expiryDate,
                Validity = new DocRValiditySummary
                {
                    Valid = validity.valid,
                    Invalid = validity.invalid
                },
                DocumentPosition = documentPosition
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

    private static DocumentPositionInfo? ExtractDocumentPosition(JsonElement root)
    {
        var node = FindFirstObjectByName(root, "DocumentPosition");
        if (!node.HasValue)
        {
            node = FindFirstObjectByNameIgnoreCase(root, "DocumentPosition");
        }

        if (!node.HasValue)
        {
            return null;
        }

        var raw = ParseDocumentPositionRaw(node.Value);
        var region = BuildRegionFromCorners(raw?.LeftTop, raw?.RightTop, raw?.RightBottom, raw?.LeftBottom);
        var info = new DocumentPositionInfo
        {
            Raw = raw,
            Region = region
        };

        info = info with
        {
            Interpretation = BuildInterpretation(info),
            Verdict = BuildVerdict(info),
            UserMessage = BuildUserMessage(info)
        };

        return info;
    }

    private static DocumentPositionRaw ParseDocumentPositionRaw(JsonElement element)
    {
        return new DocumentPositionRaw
        {
            Angle = ReadDouble(element, "Angle"),
            Center = ReadPoint(element, "Center"),
            Dpi = ReadInt(element, "Dpi"),
            Height = ReadInt(element, "Height"),
            Width = ReadInt(element, "Width"),
            Inverse = ReadInt(element, "Inverse"),
            ObjArea = ReadDouble(element, "ObjArea"),
            ObjIntAngleDev = ReadDouble(element, "ObjIntAngleDev"),
            PerspectiveTr = ReadInt(element, "PerspectiveTr"),
            ResultStatus = ReadInt(element, "ResultStatus"),
            DocFormat = ReadInt(element, "docFormat"),
            LeftTop = ReadPoint(element, "LeftTop"),
            RightTop = ReadPoint(element, "RightTop"),
            RightBottom = ReadPoint(element, "RightBottom"),
            LeftBottom = ReadPoint(element, "LeftBottom")
        };
    }

    private async Task<(IdentityDocumentInfo? info, int? statusCode, string? error, string? details)> ProcessDocumentImageAsync(string base64)
    {
        var request = new DocumentProcessRequest
        {
            Images = new List<DocumentImage> { new(base64, null) }
        };

        var payload = BuildProcessPayload(request);
        var response = await SendToDocRRawAsync(payload);

        if (!string.IsNullOrWhiteSpace(response.error))
        {
            return (null, response.statusCode, response.error, response.details);
        }

        var info = ExtractDocumentInfo(response.json ?? string.Empty);
        return (info, response.statusCode, null, null);
    }

    private async Task<(string? json, int? statusCode, string? error, string? details)> SendToDocRRawAsync(object payload)
    {
        var options = _optionsAccessor.Value;
        var client = _httpClientFactory.CreateClient("DocR");

        using var response = await client.PostAsJsonAsync(options.ProcessEndpoint, payload);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return (null, (int)response.StatusCode, "DocR process request failed.", content);
        }

        return (content, (int)response.StatusCode, null, null);
    }

    private static DocumentRegion? BuildRegionFromCorners(
        DocumentPoint? leftTop,
        DocumentPoint? rightTop,
        DocumentPoint? rightBottom,
        DocumentPoint? leftBottom)
    {
        if (leftTop is null && rightTop is null && rightBottom is null && leftBottom is null)
        {
            return null;
        }

        var points = new List<DocumentPoint>();
        if (leftTop is not null)
        {
            points.Add(leftTop);
        }

        if (rightTop is not null)
        {
            points.Add(rightTop);
        }

        if (rightBottom is not null)
        {
            points.Add(rightBottom);
        }

        if (leftBottom is not null)
        {
            points.Add(leftBottom);
        }

        return new DocumentRegion
        {
            LeftTop = leftTop,
            RightTop = rightTop,
            RightBottom = rightBottom,
            LeftBottom = leftBottom,
            Points = points
        };
    }

    private static DocumentPositionInterpretation BuildInterpretation(DocumentPositionInfo info)
    {
        var raw = info.Raw;
        return new DocumentPositionInterpretation
        {
            ResultStatus = BuildResultStatusInterpretation(raw?.ResultStatus),
            ObjArea = BuildObjAreaInterpretation(raw?.ObjArea),
            PerspectiveTr = BuildPerspectiveInterpretation(raw?.PerspectiveTr),
            Angle = BuildAngleInterpretation(raw?.Angle),
            Inverse = BuildInverseInterpretation(raw?.Inverse),
            DocFormat = BuildDocFormatInterpretation(raw?.DocFormat),
            Center = BuildCenterInterpretation(raw?.Center),
            WidthHeight = BuildWidthHeightInterpretation(raw?.Width, raw?.Height)
        };
    }

    private static DocumentPositionVerdict BuildVerdict(DocumentPositionInfo info)
    {
        var raw = info.Raw;
        var reasons = new List<string>();

        if (!raw?.ResultStatus.HasValue ?? true)
        {
            reasons.Add("ResultStatusMissing");
        }
        else if (raw.ResultStatus != 1)
        {
            reasons.Add("ResultStatusNotOK");
        }

        if (!raw?.ObjArea.HasValue ?? true)
        {
            reasons.Add("ObjAreaMissing");
        }
        else if (raw.ObjArea < 50)
        {
            reasons.Add("ObjAreaTooSmall");
        }
        else if (raw.ObjArea < 70)
        {
            reasons.Add("ObjAreaBorderline");
        }

        if (raw?.PerspectiveTr == 0)
        {
            reasons.Add("PerspectiveNotOK");
        }

        if (raw?.Inverse == 1)
        {
            reasons.Add("ImageInverted");
        }

        if (raw?.Angle.HasValue == true && Math.Abs(raw.Angle.Value) > 10)
        {
            reasons.Add("RotationTooLarge");
        }

        if (reasons.Count == 0 && raw is null)
        {
            reasons.Add("InsufficientData");
        }

        return new DocumentPositionVerdict
        {
            IsCorrectFraming = reasons.Count == 0,
            Reasons = reasons
        };
    }

    private static string BuildUserMessage(DocumentPositionInfo info)
    {
        var verdict = info.Verdict;
        if (verdict is null)
        {
            return "Unable to evaluate framing; please try again.";
        }

        if (verdict.IsCorrectFraming)
        {
            return "Great framing. Keep the document centered and fully inside the frame.";
        }

        var reasons = verdict.Reasons;
        if (reasons.Contains("ObjAreaTooSmall"))
        {
            return "Please move the document closer to fill more of the frame.";
        }

        if (reasons.Contains("ObjAreaBorderline"))
        {
            return "Please move the document slightly closer and minimize background.";
        }

        if (reasons.Contains("PerspectiveNotOK"))
        {
            return "Please hold the document flat to the camera.";
        }

        if (reasons.Contains("RotationTooLarge"))
        {
            return "Please rotate the document to be level.";
        }

        if (reasons.Contains("ImageInverted"))
        {
            return "Please flip the document to the correct orientation.";
        }

        if (reasons.Contains("ResultStatusNotOK"))
        {
            return "Please make sure the entire document is visible, well lit, and centered.";
        }

        return "Unable to evaluate framing; please try again.";
    }

    private static DocumentPositionInterpretationEntry<int?> BuildResultStatusInterpretation(int? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<int?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        var description = value.Value switch
        {
            1 => "OK",
            0 => "Failed",
            2 => "Borderline / not ideal framing. Not fully OK.",
            _ => "Unknown result status."
        };

        return new DocumentPositionInterpretationEntry<int?>
        {
            Value = value,
            Description = description
        };
    }

    private static DocumentPositionInterpretationEntry<double?> BuildObjAreaInterpretation(double? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<double?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        var description = value.Value switch
        {
            >= 70 => $"Document covers {Math.Round(value.Value, 2)}% of the image. Good full-frame coverage.",
            >= 50 => $"Document covers {Math.Round(value.Value, 2)}% of the image. Borderline coverage; recommended >= 70%.",
            _ => $"Document covers {Math.Round(value.Value, 2)}% of the image. Too much background; recommended >= 70%."
        };

        return new DocumentPositionInterpretationEntry<double?>
        {
            Value = value,
            Description = description
        };
    }

    private static DocumentPositionInterpretationEntry<int?> BuildPerspectiveInterpretation(int? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<int?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        var description = value.Value switch
        {
            1 => "Perspective is acceptable (no strong distortion).",
            0 => "Perspective is not acceptable (strong distortion).",
            2 => "Perspective check not performed.",
            _ => "Unknown perspective status."
        };

        return new DocumentPositionInterpretationEntry<int?>
        {
            Value = value,
            Description = description
        };
    }

    private static DocumentPositionInterpretationEntry<double?> BuildAngleInterpretation(double? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<double?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        var angle = Math.Abs(value.Value);
        var description = angle switch
        {
            <= 2 => "Small rotation angle (acceptable).",
            <= 10 => "Noticeable rotation; try to align the document.",
            _ => "Large rotation angle; please rotate the document."
        };

        return new DocumentPositionInterpretationEntry<double?>
        {
            Value = value,
            Description = description
        };
    }

    private static DocumentPositionInterpretationEntry<int?> BuildInverseInterpretation(int? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<int?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        var description = value.Value switch
        {
            0 => "Image is not inverted.",
            1 => "Image appears inverted.",
            _ => "Unknown inversion status."
        };

        return new DocumentPositionInterpretationEntry<int?>
        {
            Value = value,
            Description = description
        };
    }

    private static DocumentPositionInterpretationEntry<int?> BuildDocFormatInterpretation(int? value)
    {
        if (!value.HasValue)
        {
            return new DocumentPositionInterpretationEntry<int?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        return new DocumentPositionInterpretationEntry<int?>
        {
            Value = value,
            Description = "Detected document format code."
        };
    }

    private static DocumentPositionInterpretationEntry<DocumentPoint?> BuildCenterInterpretation(DocumentPoint? value)
    {
        return new DocumentPositionInterpretationEntry<DocumentPoint?>
        {
            Value = value,
            Description = value is null
                ? "Not provided by server."
                : "Document center point in image coordinates."
        };
    }

    private static DocumentPositionInterpretationEntry<DocumentWidthHeight?> BuildWidthHeightInterpretation(int? width, int? height)
    {
        if (!width.HasValue && !height.HasValue)
        {
            return new DocumentPositionInterpretationEntry<DocumentWidthHeight?>
            {
                Value = null,
                Description = "Not provided by server."
            };
        }

        return new DocumentPositionInterpretationEntry<DocumentWidthHeight?>
        {
            Value = new DocumentWidthHeight { Width = width, Height = height },
            Description = "Detected document size in pixels (in the input image coordinate space)."
        };
    }

    private static DocumentPoint? ReadPoint(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var pointElement) ||
            pointElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var x = ReadDouble(pointElement, "x");
        var y = ReadDouble(pointElement, "y");

        if (!x.HasValue && !y.HasValue)
        {
            return null;
        }

        return new DocumentPoint { X = x, Y = y };
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.TryGetDouble(out var doubleValue))
            {
                return (int)Math.Round(doubleValue);
            }
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
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
            var best = FindBestImageBase64(root);
            if (!string.IsNullOrWhiteSpace(best))
            {
                return best;
            }

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

    private static string? FindBestImageBase64(JsonElement root)
    {
        var candidates = new List<(string value, string path)>();
        CollectBase64Candidates(root, "root", candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .Select(item => (item.value, score: ScoreImageCandidate(item.value, item.path)))
            .OrderByDescending(item => item.score)
            .FirstOrDefault();

        return best.value;
    }

    private static void CollectBase64Candidates(JsonElement element, string path, List<(string value, string path)> output)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var nextPath = $"{path}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (IsProbablyBase64(value))
                    {
                        output.Add((value ?? string.Empty, nextPath));
                    }
                }

                CollectBase64Candidates(prop.Value, nextPath, output);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                var nextPath = $"{path}[{index}]";
                CollectBase64Candidates(item, nextPath, output);
                index++;
            }
        }
    }

    private static double ScoreImageCandidate(string base64, string path)
    {
        var score = 0.0;
        var lowerPath = path.ToLowerInvariant();

        if (lowerPath.Contains("portrait") || lowerPath.Contains("face"))
        {
            score += 50;
        }

        if (lowerPath.Contains("mrz") || lowerPath.Contains("signature"))
        {
            score -= 20;
        }

        if (lowerPath.Contains("logo") || lowerPath.Contains("emblem") || lowerPath.Contains("flag"))
        {
            score -= 30;
        }

        score += Math.Min(base64.Length / 1000.0, 50);
        return score;
    }

    private static IdentityDocumentInfo ExtractDocumentInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var fields = ExtractDocVisualFields(root);
            var surname = GetFieldValue(fields, "Surname", "Last Name", "Family Name");
            var name = GetFieldValue(fields, "Given Names", "Given Name", "First Name", "Name", "Full Name");
            var documentNumber = GetFieldValue(fields, "Document Number", "Document No.", "Doc Number", "Passport Number", "Passport No.", "ID Number", "Identity Number");
            var dateOfBirth = GetFieldValue(fields, "Date of Birth", "Birth Date", "DOB");
            var gender = GetFieldValue(fields, "Sex", "Gender");
            var mrzText = ExtractMrzRawText(root);
            var portrait = ExtractDocumentPortraitBase64(json);

            return new IdentityDocumentInfo
            {
                Name = name,
                Surname = surname,
                DocumentNumber = documentNumber,
                DateOfBirth = dateOfBirth,
                Gender = gender,
                MrzText = mrzText,
                PortraitImageBase64 = CleanBase64(portrait ?? string.Empty),
                RawResponseJson = json
            };
        }
        catch
        {
            return new IdentityDocumentInfo
            {
                RawResponseJson = json
            };
        }
    }

    private static string? ExtractMrzRawText(JsonElement root)
    {
        var direct = FindFirstString(root, new[]
        {
            "mrz",
            "MRZ",
            "mrzText",
            "MRZText",
            "mrzString",
            "MRZString",
            "rawMRZ",
            "rawMrz",
            "mrzRaw",
            "MRZRaw",
            "mrzTextRaw"
        });

        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var mrzNode = FindFirstObjectByNameIgnoreCase(root, "MRZ") ?? FindFirstObjectByNameIgnoreCase(root, "Mrz");
        if (mrzNode.HasValue)
        {
            var candidate = FindFirstString(mrzNode.Value, new[] { "Text", "text", "Raw", "raw", "value", "Value" });
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static double? NormalizeSimilarityScore(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var number = value.Value;
        if (number < 0)
        {
            return null;
        }

        if (number > 1 && number <= 100)
        {
            return Math.Round(number / 100.0, 4);
        }

        return Math.Round(number, 4);
    }

    private static bool CompareNormalized(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return NormalizeValue(left) == NormalizeValue(right);
    }

    private static bool CompareNames(IdentityDocumentInfo first, IdentityDocumentInfo second)
    {
        var leftName = NormalizeValue(first.Name);
        var rightName = NormalizeValue(second.Name);
        var leftSurname = NormalizeValue(first.Surname);
        var rightSurname = NormalizeValue(second.Surname);

        if (!string.IsNullOrWhiteSpace(leftName) && !string.IsNullOrWhiteSpace(rightName) &&
            !string.IsNullOrWhiteSpace(leftSurname) && !string.IsNullOrWhiteSpace(rightSurname))
        {
            return leftName == rightName && leftSurname == rightSurname;
        }

        var fullLeft = NormalizeValue($"{first.Name} {first.Surname}");
        var fullRight = NormalizeValue($"{second.Name} {second.Surname}");

        if (!string.IsNullOrWhiteSpace(fullLeft) && !string.IsNullOrWhiteSpace(fullRight))
        {
            return fullLeft == fullRight;
        }

        return false;
    }

    private static bool CompareDates(string? left, string? right)
    {
        var leftNorm = NormalizeDate(left);
        var rightNorm = NormalizeDate(right);
        if (string.IsNullOrWhiteSpace(leftNorm) || string.IsNullOrWhiteSpace(rightNorm))
        {
            return false;
        }

        return leftNorm == rightNorm;
    }

    private static string NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToUpperInvariant();
        var filtered = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
        return filtered;
    }

    private static string NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits;
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
