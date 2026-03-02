using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RegulaWebApp.Services;

public interface IDocumentFraudService
{
    Task<IActionResult> DetectDocumentFraud(HttpRequest request);
}
