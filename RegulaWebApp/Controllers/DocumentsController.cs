using Microsoft.AspNetCore.Mvc;
using RegulaWebApp.Services;

namespace RegulaWebApp.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    [HttpPost("process")]
    public async Task<IActionResult> ProcessDocument(
        IDocumentProcessingService documentProcessingService)
    {
        return await documentProcessingService.ProcessDocument(Request);
    }

    [HttpPost("fraud-detection")]
    public async Task<IActionResult> FraudDetection(
        IDocumentProcessingService documentProcessingService)
    {
        return await documentProcessingService.FraudDetection(Request);
    }

    [HttpPost("verify-identity")]
    public async Task<IActionResult> VerifyIdentity(
        IDocumentProcessingService documentProcessingService)
    {
        return await documentProcessingService.VerifyIdentity(Request);
    }
}
