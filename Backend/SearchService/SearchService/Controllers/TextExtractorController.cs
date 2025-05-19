using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SearchService.Services;
using System.IO;
using System.Threading.Tasks;

namespace SearchService.Controllers
{
    [ApiController]
    [Route("api/text-extract")]
    public class TextExtractController : ControllerBase
    {
        private readonly ITextExtractorService _textExtractor;
        private readonly ILogger<TextExtractController> _logger;

        public TextExtractController(ITextExtractorService textExtractor, ILogger<TextExtractController> logger)
        {
            _textExtractor = textExtractor;
            _logger = logger;
        }

        [HttpPost("extract")]
        public async Task<IActionResult> ExtractText([FromForm] IFormFile file, [FromForm] string? contentType)
        {
            if (file == null)
                return BadRequest("File is required.");

            if (!_textExtractor.SupportsContentType(contentType, file.FileName))
                return Ok(string.Empty);

            using var stream = file.OpenReadStream();
            var text = await _textExtractor.ExtractTextAsync(stream, contentType, file.FileName);
            if (text == null) text = string.Empty;
            return Ok(text);
        }
    }
}
