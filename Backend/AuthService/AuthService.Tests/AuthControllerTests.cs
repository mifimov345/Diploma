using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestPlatform.TestHost;

public class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>  // Используем Program напрямую
{
    private readonly HttpClient _client;

    public AuthControllerTests(WebApplicationFactory<Program> factory)  // Используем Program напрямую
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ReturnsOk_WhenValidCredentials()
    {
        // Arrange
        var loginRequest = new { username = "admin", password = "password" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.EnsureSuccessStatusCode();  // 200-299
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenInvalidCredentials()
    {
        // Arrange
        var loginRequest = new { username = "admin", password = "wrongpassword" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.False(response.IsSuccessStatusCode); // 401
    }
}
