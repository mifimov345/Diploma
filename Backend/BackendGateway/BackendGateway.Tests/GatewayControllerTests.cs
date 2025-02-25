using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.TestHost;

namespace BackendGateway.Tests
{
    public class GatewayControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public GatewayControllerTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task ForwardRequestToAuthService_ReturnsSuccess()
        {
            // Act
            var response = await _client.GetAsync("/api/gateway/auth/login");

            // Assert
            response.EnsureSuccessStatusCode();  // 200-299
        }

        [Fact]
        public async Task ForwardRequestToFileService_ReturnsSuccess()
        {
            // Act
            var response = await _client.GetAsync("/api/gateway/file/upload");

            // Assert
            response.EnsureSuccessStatusCode();  // 200-299
        }
    }
}
