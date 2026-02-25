namespace RegulaWebApp.Models;

public record DetectFaceRequest(string? ImageBase64);
public record FaceMatchRequest(string? ImageBase64_1, string? ImageBase64_2);

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
