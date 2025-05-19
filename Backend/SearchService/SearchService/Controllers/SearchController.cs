using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SearchService.Services;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Hosting;

namespace SearchService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly ISearcherService _searcherService;
        private readonly ILogger<SearchController> _logger;
        private readonly IWebHostEnvironment _environment;

        public SearchController(
            ISearcherService searcherService,
            ILogger<SearchController> logger,
            IWebHostEnvironment environment)
        {
            _searcherService = searcherService ?? throw new ArgumentNullException(nameof(searcherService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string term, [FromQuery] int? userId)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest(new ProblemDetails { Title = "Search term is required.", Status = StatusCodes.Status400BadRequest });
            }

            try
            {
                var results = await _searcherService.SearchFilesAsync(term, userId);
                return Ok(results ?? Enumerable.Empty<Guid>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during search execution via SearcherService for term: {SearchTerm}", term);
                return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
                {
                    Title = "An internal error occurred during search.",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = _environment.IsDevelopment() ? ex.ToString() : "An internal error occurred while processing your request."
                });
            }
        }
    }
}