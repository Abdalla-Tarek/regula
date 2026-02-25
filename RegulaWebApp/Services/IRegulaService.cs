using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using RegulaWebApp.Models;

namespace RegulaWebApp.Services;

public interface IRegulaService
{
    Task<IActionResult> DetectFace(HttpRequest request);
    Task<IActionResult> LivenessDetection(LivenessRequest body);
    Task<IActionResult> FaceMatch(HttpRequest request);
}
