using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace RegulaWebApp.Services;

public interface IDocumentProcessingService
{
    Task<IActionResult> ProcessDocument(HttpRequest request);
    Task<IActionResult> FraudDetection(HttpRequest request);
    Task<IActionResult> VerifyIdentity(HttpRequest request);
    Task<IActionResult> ComparePassportsAsync(HttpRequest request);
}
