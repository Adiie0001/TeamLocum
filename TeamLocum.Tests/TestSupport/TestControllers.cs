using Microsoft.AspNetCore.Mvc;

namespace TeamLocum.Tests.TestSupport;

/// <summary>Only registered in the test host; gives the pipeline an endpoint that fails the way a real bug would.</summary>
[Route("test")]
public sealed class FailureController : Controller
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("internal detail that must never reach a client");
}
