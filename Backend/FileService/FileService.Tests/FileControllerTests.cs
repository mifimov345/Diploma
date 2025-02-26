using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;
using YourTestsNamespace; // Пространство имен для CustomWebApplicationFactory

namespace FileService.Tests
{
    public class FileControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public FileControllerTests(CustomWebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task UploadFile_ReturnsOk()
        {
            using var form = new MultipartFormDataContent();
            // Создаем пример файла
            var fileContent = new StringContent("This is a test file content");
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
            form.Add(fileContent, "file", "test.txt");

            // Предположим, эндпоинт загрузки файла — /api/file/upload
            var response = await _client.PostAsync("/api/file/upload", form);
            response.EnsureSuccessStatusCode();
        }
    }
}
