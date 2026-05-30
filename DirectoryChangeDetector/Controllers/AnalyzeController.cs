using DirectoryChangeDetector.Exceptions;
using DirectoryChangeDetector.Interfaces;
using DirectoryChangeDetector.Models;
using Microsoft.AspNetCore.Mvc;

namespace DirectoryChangeDetector.Controllers;

[ApiController]
[Route("api/v1/analyze")]
public sealed class AnalyzeController(IDirectoryAnalyzer analyzer) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ChangeReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChangeReport>> Analyze(
        [FromBody] AnalyzeRequest request, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "A non-empty 'path' is required.");
        }

        try
        {
            var report = await analyzer.AnalyzeAsync(request.Path, cancellationToken);
            return Ok(report);
        }
        catch (DirectoryAnalysisException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Analysis failed",
                detail: ex.Message);
        }
    }
}

public sealed record AnalyzeRequest(string Path);
