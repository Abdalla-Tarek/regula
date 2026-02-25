using Microsoft.AspNetCore.Mvc;
using RegulaWebApp.Models;
using RegulaWebApp.Services;

namespace RegulaWebApp.Controllers;

[ApiController]
[Route("api")]
public class RegulaController : ControllerBase
{
    [HttpPost("detect-face")]
    public async Task<IActionResult> DetectFace(
        IRegulaService regulaService)
    {
        return await regulaService.DetectFace(Request);
    }

    [HttpPost("icao-detect")]
    public async Task<IActionResult> DetectIcao(
        IRegulaService regulaService)
    {
        return await regulaService.DetectIcao(Request);
    }

    [HttpPost("liveness-detection")]
    public async Task<IActionResult> LivenessDetection(
        [FromBody] LivenessRequest body,
        IRegulaService regulaService)
    {
        return await regulaService.LivenessDetection(body);
    }

    [HttpPost("face-match")]
    public async Task<IActionResult> FaceMatch(
        IRegulaService regulaService)
    {
        return await regulaService.FaceMatch(Request);
    }
}
