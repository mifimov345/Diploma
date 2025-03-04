using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;

namespace BackendGateway.Controllers
{
    /// <summary>
    /// Контроллер-шлюз для переадресации запросов к другим сервисам.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GatewayController : ControllerBase
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Конструктор, принимающий фабрику HTTP-клиентов для создания клиента.
        /// </summary>
        /// <param name="httpClientFactory">Фабрика HTTP-клиентов.</param>
        public GatewayController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        /// <summary>
        /// Переадресует GET-запрос к целевому сервису.
        /// </summary>
        /// <param name="service">Название сервиса ("auth" или "file").</param>
        /// <param name="path">Путь внутри сервиса, к которому будет переадресован запрос.</param>
        /// <returns>Возвращает содержимое ответа от целевого сервиса.</returns>
        /// <remarks>
        /// Пример вызова:
        /// GET: <c>api/gateway/auth/login</c> – переадресация запроса к AuthService.
        /// </remarks>
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

        /// <summary>
        /// Переадресует POST-запрос с передачей payload к целевому сервису.
        /// </summary>
        /// <param name="service">Название сервиса ("auth" или "file").</param>
        /// <param name="path">Путь внутри сервиса, к которому будет переадресован запрос.</param>
        /// <param name="payload">Объект данных, передаваемый в теле запроса.</param>
        /// <returns>Возвращает содержимое ответа от целевого сервиса.</returns>
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
