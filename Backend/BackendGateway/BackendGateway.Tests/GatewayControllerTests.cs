using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BackendGateway.Tests
{
    // »спользуем наш кастомный тип фабрики; тип Program должен быть доступен (public partial class Program { } в Program.cs)
    public class GatewayControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public GatewayControllerTests(CustomWebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetStatus_ReturnsOk()
        {
            // ≈сли ваш контроллер ожидает маршрут вида: GET /api/gateway/{service}/{*path}
            // например, /api/gateway/auth/status, используем такой запрос.
            var response = await _client.GetAsync("/api/gateway/auth/status");

            // ≈сли ответ не успешный, выводим его содержимое дл€ диагностики.
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.True(false, $"«апрос вернул статус {response.StatusCode} с телом: {errorContent}");
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("Test response from BackendGateway", content);
        }
    }
}
