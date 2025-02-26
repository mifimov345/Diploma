using Xunit;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;
using AuthService.Tests;

public class AuthControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AuthControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        // Создаем клиент через нашу кастомную фабрику
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
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenInvalidCredentials()
    {
        // Arrange
        var loginRequest = new { username = "admin", password = "wrongpassword" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.False(response.IsSuccessStatusCode);
    }
}
