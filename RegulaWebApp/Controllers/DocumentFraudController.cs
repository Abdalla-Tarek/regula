using Microsoft.AspNetCore.Mvc;
using RegulaWebApp.Services;

namespace RegulaWebApp.Controllers;

[ApiController]
[Route("api/document-fraud")]
public class DocumentFraudController : ControllerBase
{
    [HttpPost("detect")]
    public async Task<IActionResult> DetectDocumentFraud(
        IDocumentFraudService documentFraudService)
    {
        return await documentFraudService.DetectDocumentFraud(Request);
    }
}
