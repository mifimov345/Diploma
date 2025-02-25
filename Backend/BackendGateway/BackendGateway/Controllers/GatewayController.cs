using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;

namespace BackendGateway.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GatewayController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        public GatewayController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        // Пример GET-запроса для переадресации
        // URL: api/gateway/{service}/{*path}
        [HttpGet("{service}/{*path}")]
        public async Task<IActionResult> ForwardGet(string service, string path)
        {
            string targetUrl = service.ToLower() switch
            {
                "auth" => $"http://localhost:5001/api/auth/{path}",
                "file" => $"http://localhost:5002/api/file/{path}",
                _ => null
            };

            if (targetUrl == null)
                return BadRequest("Unknown service.");

            var response = await _httpClient.GetAsync(targetUrl);
            var content = await response.Content.ReadAsStringAsync();
            return Content(content, response.Content.Headers.ContentType?.MediaType);
        }

        // Дополнительно можно реализовать POST-переадресацию
        [HttpPost("{service}/{*path}")]
        public async Task<IActionResult> ForwardPost(string service, string path, [FromBody] object payload)
        {
            string targetUrl = service.ToLower() switch
            {
                "auth" => $"http://localhost:5001/api/auth/{path}",
                "file" => $"http://localhost:5002/api/file/{path}",
                _ => null
            };

            if (targetUrl == null)
                return BadRequest("Unknown service.");

            var response = await _httpClient.PostAsJsonAsync(targetUrl, payload);
            var content = await response.Content.ReadAsStringAsync();
            return Content(content, response.Content.Headers.ContentType?.MediaType);
        }
    }
}
