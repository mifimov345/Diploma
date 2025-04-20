using Microsoft.AspNetCore.Mvc;
using SearchService.Services;

[ApiController]
[Route("api/[controller]")]
public class IndexController : ControllerBase
{
    private readonly IIndexService _indexService;
    private readonly ITextExtractorService _textExtractor;
    private readonly IHttpClientFactory _httpClientFactory; // Для скачивания файла из FileService
    private readonly ILogger<IndexController> _logger;

    public IndexController(
        IIndexService indexService,
        ITextExtractorService textExtractor,
        IHttpClientFactory httpClientFactory,
        ILogger<IndexController> logger)
    {
        _indexService = indexService;
        _textExtractor = textExtractor;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // Вызывается из FileService после успешной загрузки файла
    [HttpPost("index")]
    public async Task<IActionResult> IndexFile([FromBody] IndexRequestModel model)
    {
        _logger.LogInformation("Received index request for File ID: {FileId}, User ID: {UserId}, Name: {FileName}, Type: {ContentType}",
         model.FileId, model.UserId, model.OriginalName ?? "N/A", model.ContentType ?? "N/A");
        if (model.FileId == Guid.Empty) return BadRequest("FileId is required.");
        if (string.IsNullOrEmpty(model.OriginalName)) _logger.LogWarning("OriginalName is missing in index request for File ID {FileId}", model.FileId);

        // 1. Скачать файл из FileService (нужен эндпоинт в FileService, доступный только внутри сети Docker)
        //    ИЛИ FileService может сам отправлять контент файла сюда.
        //    Предположим, скачиваем:
        Stream fileStream = null;
        string extractedText = null;
        try
        {
            // ВАЖНО: FileService должен предоставлять способ скачать файл по ID без обычной проверки прав,
            // т.к. этот запрос идет от доверенного сервиса SearchService. Либо FileService должен
            // сам отправлять контент при уведомлении.
            // Используем прямой вызов по имени сервиса Docker.
            var client = _httpClientFactory.CreateClient();
            // Нужен URL для скачивания ВНУТРИ сети Docker, без проверки прав этим эндпоинтом
            // Например, создайте в FileController специальный эндпоинт вроде [HttpGet("internal/download/{id}")]
            var downloadUrl = $"http://fileservice/api/file/internal/download/{model.FileId}"; // ПРИМЕР URL!
            _logger.LogInformation("Downloading file from {DownloadUrl}", downloadUrl);

            var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download file {FileId} from FileService. Status: {StatusCode}", model.FileId, response.StatusCode);
                // Можно вернуть ошибку или просто не индексировать
                return StatusCode((int)response.StatusCode, $"Failed to download file from FileService: {response.ReasonPhrase}");
            }

            fileStream = await response.Content.ReadAsStreamAsync();
            _logger.LogInformation("File {FileId} downloaded successfully.", model.FileId);

            // 2. Извлечь текст
            // Передаем пустые contentType/fileName, т.к. Tika их сам определит по потоку
            extractedText = await _textExtractor.ExtractTextAsync(fileStream, model.ContentType, model.OriginalName);

            if (string.IsNullOrEmpty(extractedText))
            {
                _logger.LogWarning("No text could be extracted from file {FileId}.", model.FileId);
                // Не индексируем, но возвращаем ОК, т.к. процесс прошел без ошибок
                return Ok($"No text content extracted for file {model.FileId}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading or extracting text for file {FileId}", model.FileId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Error processing file for indexing.");
        }
        finally
        {
            fileStream?.Dispose(); // Убедимся, что поток закрыт
        }


        // 3. Отправить текст на индексацию
        if (!string.IsNullOrEmpty(extractedText))
        {
            try
            {
                await _indexService.IndexFileAsync(model.FileId, model.UserId, extractedText);
                _logger.LogInformation("File {FileId} sent for indexing.", model.FileId);
                return Ok($"File {model.FileId} indexed successfully.");
            }
            catch (Exception exIndex)
            {
                _logger.LogError(exIndex, "Error during indexing call for file {FileId}", model.FileId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error indexing file content.");
            }
        }
        else
        {
            // Случай, когда текст не извлекся, но ошибки не было
            return Ok($"File {model.FileId} processed, but no text content found to index.");
        }
    }

    // Эндпоинт для удаления из индекса (вызывается из FileService при удалении файла)
    [HttpDelete("index/{fileId}")]
    public async Task<IActionResult> DeleteIndex(Guid fileId)
    {
        _logger.LogInformation("Received delete index request for File ID: {FileId}", fileId);
        if (fileId == Guid.Empty) return BadRequest("FileId is required.");

        try
        {
            await _indexService.DeleteFileAsync(fileId);
            return NoContent(); // Успешно удалено (или не найдено в индексе)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId} from index.", fileId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Error removing file from index.");
        }
    }
}

// Модель запроса на индексацию
public class IndexRequestModel
{
    public Guid FileId { get; set; }
    public int UserId { get; set; }
    public string OriginalName { get; set; }
    public string ContentType { get; set; }
}