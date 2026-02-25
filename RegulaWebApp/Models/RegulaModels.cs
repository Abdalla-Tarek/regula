using System.Text.Json.Serialization;

namespace RegulaWebApp.Models;

public record DetectFaceRequest(string? ImageBase64);
public record FaceMatchRequest(string? ImageBase64_1, string? ImageBase64_2);
public record DocumentImage(string Base64, string? Format);

public record DocumentProcessRequest
{
    public List<DocumentImage> Images { get; init; } = new();
    public string? Scenario { get; init; }
    public string? Tag { get; init; }
    public string? LivePortraitBase64 { get; init; }
}

public record DocRProcessRequest(
    [property: JsonPropertyName("processParam")] DocRProcessParam ProcessParam,
    [property: JsonPropertyName("List")] List<DocRListItem> List,
    [property: JsonPropertyName("livePortrait"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LivePortrait);

public record DocRProcessParam(
    [property: JsonPropertyName("scenario")] string Scenario,
    [property: JsonPropertyName("authParams"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DocRAuthParams? AuthParams,
    [property: JsonPropertyName("useFaceApi"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? UseFaceApi,
    [property: JsonPropertyName("faceApi"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DocRFaceApiConfig? FaceApi,
    [property: JsonPropertyName("checkLiveness"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CheckLiveness,
    [property: JsonPropertyName("oneShotIdentification"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? OneShotIdentification);

public record DocRAuthParams(
    [property: JsonPropertyName("checkLiveness")] bool CheckLiveness);

public record DocRFaceApiConfig(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("threshold"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Threshold);

public record DocRListItem(
    [property: JsonPropertyName("ImageData")] DocRImageData ImageData);

public record DocRImageData(
    [property: JsonPropertyName("image")] string Image);

public record LivenessRequest
{
    public List<string> Frames { get; init; } = new();
    public string? TransactionId { get; init; }
}

public record FaceSummary(Dictionary<string, object?> Details);
public record FaceMatchSummary(double? Similarity, double? Score);
public record LivenessSummary(string? LivenessStatus, double? Score);

public record RegulaOptions
{
    public string BaseUrl { get; init; } = "https://fr.mohre.gov.ae";
    public string DetectEndpoint { get; init; } = "/api/detect";
    public string MatchEndpoint { get; init; } = "/api/match";
    public string LivenessEndpoint { get; init; } = "/api/v2/liveness";
    public string ApiKey { get; init; } = string.Empty;
    public string ApiKeyHeader { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 30;
}

public record DocROptions
{
    public string BaseUrl { get; init; } = "https://docr.mohre.gov.ae";
    public string ProcessEndpoint { get; init; } = "/api/process";
    public string FaceApiUrl { get; init; } = "https://fr.mohre.gov.ae";
    public string FaceApiMode { get; init; } = "match";
    public double? FaceApiThreshold { get; init; } = null;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiKeyHeader { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 60;
}
