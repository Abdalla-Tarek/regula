using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using RegulaWebApp.Models;

namespace RegulaWebApp.Services;

public interface IRegulaService
{
    Task<IActionResult> DetectFace(HttpRequest request);
    Task<IActionResult> DetectIcao(HttpRequest request);
    Task<IActionResult> LivenessDetection(LivenessRequest body);
    Task<IActionResult> FaceMatch(HttpRequest request);
    Task<(double? similarity, int? statusCode, string? error, string? details)> MatchFaces(string image1, string image2);
}
