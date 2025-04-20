// SearchService/Controllers/SearchController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SearchService.Services; // Убедитесь, что using для сервиса правильный
using System;
using System.Threading.Tasks;

[ApiController]
[Route("api/[controller]")] // Должно разрешиться в /api/search
public class SearchController : ControllerBase
{
    private readonly ISearcherService _searcherService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ISearcherService searcherService, ILogger<SearchController> logger)
    {
        _searcherService = searcherService;
        _logger = logger;
    }

    // Убедитесь, что это GET и атрибуты [FromQuery] на месте
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string term, [FromQuery] int? userId)
    {
        _logger.LogInformation("--- SearchController: Received search request. Term='{SearchTerm}', UserID filter={UserIdFilter}", term, userId?.ToString() ?? "None");

        if (string.IsNullOrWhiteSpace(term))
        {
            _logger.LogWarning("Search term is required.");
            // Возвращаем Problem Details для 400
            return BadRequest(new ProblemDetails { Title = "Search term is required.", Status = StatusCodes.Status400BadRequest });
        }

        try
        {
            var results = await _searcherService.SearchFilesAsync(term, userId);
            _logger.LogInformation("Search completed. Found {ResultCount} matching file IDs.", results.Count());
            return Ok(results); // Возвращаем список ID
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search execution for term: {SearchTerm}", term);
            // Возвращаем Problem Details для 500
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = "An error occurred during search.", Status = StatusCodes.Status500InternalServerError, Detail = ex.Message });
        }
    }
}