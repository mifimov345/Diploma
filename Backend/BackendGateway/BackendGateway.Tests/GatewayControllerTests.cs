using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BackendGateway.Tests
{
    // ���������� ��� ��������� ��� �������; ��� Program ������ ���� �������� (public partial class Program { } � Program.cs)
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
            // ���� ��� ���������� ������� ������� ����: GET /api/gateway/{service}/{*path}
            // ��������, /api/gateway/auth/status, ���������� ����� ������.
            var response = await _client.GetAsync("/api/gateway/auth/status");

            // ���� ����� �� ��������, ������� ��� ���������� ��� �����������.
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.True(false, $"������ ������ ������ {response.StatusCode} � �����: {errorContent}");
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("Test response from BackendGateway", content);
        }
    }
}
