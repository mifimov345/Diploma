using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FileService.Tests
{
    public class FileControllerTests : IClassFixture<WebApplicationFactory<FileService.Program>>
    {
        private readonly HttpClient _client;

        public FileControllerTests(WebApplicationFactory<FileService.Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task UploadFile_ReturnsOk_WhenFileIsUploaded()
        {
            // Arrange
            var formData = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(System.IO.File.ReadAllBytes("path_to_some_file"));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            formData.Add(fileContent, "file", "testfile.txt");

            // Act
            var response = await _client.PostAsync("/api/file/upload", formData);

            // Assert
            response.EnsureSuccessStatusCode();  // 200-299
        }
    }
}
