using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers; // Для ContentDispositionHeaderValue
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using FileService.Models;
using FileService.Services;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Net.Http;      // Для HttpClientFactory
using System.Text;          // Для Encoding
using System.Text.Json;     // Для JsonSerializer
using Microsoft.AspNetCore.Http; // Для StatusCodes

namespace FileService.Controllers
{
    // Определим константы ролей здесь для удобства
    public static class AppRoles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string User = "User";
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Все методы требуют аутентификации по умолчанию
    public class FileController : ControllerBase
    {
        private readonly IFileMetadataService _metadataService;
        private readonly ILogger<FileController> _logger;
        private readonly string _uploadBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        private readonly IHttpClientFactory _httpClientFactory; // Для уведомления SearchService

        public FileController(
            IFileMetadataService metadataService,
            ILogger<FileController> logger,
            IHttpClientFactory httpClientFactory) // Внедряем зависимости
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            try
            {
                // Убедимся, что базовая папка существует при запуске
                if (!Directory.Exists(_uploadBasePath))
                {
                    Directory.CreateDirectory(_uploadBasePath);
                    _logger.LogInformation("Upload base directory created at: {Path}", _uploadBasePath);
                }
                else
                {
                    _logger.LogInformation("Upload base directory already exists at: {Path}", _uploadBasePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create or access upload base directory at: {Path}", _uploadBasePath);
                // В реальном приложении это может быть критичной ошибкой, возможно стоит выбросить исключение
                // throw new ApplicationException($"Failed to initialize upload directory '{_uploadBasePath}'. Service cannot start.", ex);
            }
        }

        // --- Загрузка файла ---
        [HttpPost("upload")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")] // Все авторизованные могут загружать
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            if (model.File == null || model.File.Length == 0)
            {
                _logger.LogWarning("UploadFile attempt with no file.");
                return BadRequest("No file uploaded.");
            }

            // Ограничение размера файла (пример: 100MB)
            long maxFileSize = 100 * 1024 * 1024;
            if (model.File.Length > maxFileSize)
            {
                _logger.LogWarning("UploadFile attempt by User (ClaimsPrincipal Identity: {Identity}) with file size {FileSize} exceeding limit {Limit}.", User.Identity?.Name ?? "N/A", model.File.Length, maxFileSize);
                return BadRequest($"File size exceeds the {maxFileSize / 1024 / 1024} MB limit.");
            }

            int userId;
            string userPrimaryGroup;
            try
            {
                // Парсинг данных пользователя из токена
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                // Берем первую группу или 'Default'
                userPrimaryGroup = User.FindAll("group").FirstOrDefault()?.Value ?? "Default";
                _logger.LogInformation("UploadFile request by User ID {UserId}, Primary Group: {Group}", userId, userPrimaryGroup);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing user claims during file upload. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            // Формирование пути для сохранения файла
            string safeGroupName = string.Join("_", userPrimaryGroup.Split(Path.GetInvalidFileNameChars())); // Делаем имя папки безопасным
            string userDirectoryPath = Path.Combine(_uploadBasePath, safeGroupName, userId.ToString());

            try
            {
                Directory.CreateDirectory(userDirectoryPath); // Создаем директорию, если её нет
                _logger.LogDebug("Ensured user directory exists: {Path}", userDirectoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user directory for upload: {Path}", userDirectoryPath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error creating user directory.");
            }

            // Генерация уникального имени файла и полного пути
            var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(model.File.FileName)}";
            var filePath = Path.Combine(userDirectoryPath, storedFileName);
            _logger.LogInformation("Generated target file path: {FilePath}", filePath);

            FileMetadata metadata = null; // Объявляем заранее для доступа после try
            try
            {
                // Копируем файл на диск
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.File.CopyToAsync(stream);
                }
                _logger.LogInformation("File content saved successfully to {FilePath} by User ID {UserId}", filePath, userId);

                // Сохраняем метаданные в хранилище (например, в памяти или БД)
                metadata = new FileMetadata
                {
                    Id = Guid.NewGuid(),
                    OriginalName = model.File.FileName,
                    StoredFileName = storedFileName, // Важно сохранить уникальное имя
                    ContentType = model.File.ContentType,
                    SizeBytes = model.File.Length,
                    UploadedAt = DateTime.UtcNow,
                    UserId = userId,
                    UserGroup = userPrimaryGroup, // Сохраняем основную группу на момент загрузки
                    FilePath = filePath // Сохраняем полный путь для скачивания/удаления
                };
                await _metadataService.AddMetadataAsync(metadata);
                _logger.LogInformation("Metadata saved for file {FileId}", metadata.Id);

                // Асинхронно уведомляем SearchService об индексации (не ждем ответа)
                if (metadata != null && metadata.Id != Guid.Empty)
                {
                    // Используем Task.Run для запуска в фоновом потоке
                    _ = Task.Run(() => NotifySearchServiceIndexAsync(metadata.Id, metadata.UserId));
                }

                // Возвращаем успешный результат клиенту
                return Ok(new { FileId = metadata.Id, FileName = metadata.OriginalName, Group = metadata.UserGroup });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file upload processing for User ID {UserId}, Target Path: {FilePath}", userId, filePath);
                // Пытаемся удалить частично загруженный файл при ошибке
                if (System.IO.File.Exists(filePath))
                {
                    try { System.IO.File.Delete(filePath); _logger.LogWarning("Partial file deleted due to error: {FilePath}", filePath); }
                    catch (Exception delEx) { _logger.LogError(delEx, "Failed to delete partial file after error: {FilePath}", filePath); }
                }
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error during file upload.");
            }
        }

        // --- Получение списка файлов (для всех ролей, с разной фильтрацией) ---
        [HttpGet("files")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> GetFiles()
        {
            int userId;
            string userRole;
            List<string> userGroups;
            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRole = User.FindFirstValue(ClaimTypes.Role);
                userGroups = User.FindAll("group").Select(c => c.Value).ToList();
                _logger.LogInformation("GetFiles request by User ID {UserId}, Role {UserRole}", userId, userRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing user claims during GetFiles. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            IEnumerable<FileMetadata> files;
            IEnumerable<object> result; // Используем object для анонимных типов ответа

            try
            {
                // В зависимости от роли получаем соответствующий список файлов
                if (userRole == AppRoles.SuperAdmin)
                {
                    files = await _metadataService.GetAllMetadataAsync();
                    // ВА и Админ видят ID пользователя и ContentType
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                else if (userRole == AppRoles.Admin)
                {
                    files = await _metadataService.GetMetadataByUserGroupsAsync(userGroups, userId);
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                else // UserRole == AppRoles.User
                {
                    files = await _metadataService.GetMetadataByUserAsync(userId);
                    // Пользователь видит только свои файлы, UserId ему не нужен в списке
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                _logger.LogInformation("Returning {FileCount} files for User ID {UserId}", files.Count(), userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving files for User ID {UserId}", userId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving files.");
            }
        }

        // --- Поиск файлов ---
        [HttpGet("search")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> SearchFiles([FromQuery] string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                _logger.LogInformation("Search request with empty term.");
                return Ok(Enumerable.Empty<object>()); // Пустой запрос - пустой результат
            }

            int userId;
            string userRole;
            List<string> userGroups;
            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRole = User.FindFirstValue(ClaimTypes.Role);
                userGroups = User.FindAll("group").Select(c => c.Value).ToList();
                _logger.LogInformation("SearchFiles request by User ID {UserId}, Role {UserRole}, Term: '{SearchTerm}'", userId, userRole, term);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing user claims during SearchFiles. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            try
            {
                // Вызываем сервис поиска
                var files = await _metadataService.SearchMetadataAsync(term, userId, userRole, userGroups);

                // Форматируем результат аналогично GetFiles
                IEnumerable<object> result;
                if (userRole == AppRoles.SuperAdmin || userRole == AppRoles.Admin)
                {
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                else // User
                {
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.SizeBytes, f.UploadedAt, f.ContentType });
                }

                _logger.LogInformation("Search returned {FileCount} files for User ID {UserId}", files.Count(), userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching files for User ID {UserId}, Term: '{SearchTerm}'", userId, term);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while searching files.");
            }
        }

        // --- Скачивание/Предпросмотр файла ---
        [HttpGet("download/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DownloadFile(Guid id, [FromQuery] bool inline = false)
        {
            _logger.LogInformation("Download/Preview request for File ID {FileId}, Inline: {IsInline}", id, inline);
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                currentUserGroups = User.FindAll("group").Select(c => c.Value).ToList();
                _logger.LogDebug("Requesting user details - ID: {UserId}, Role: {UserRole}, Groups: [{Groups}]", currentUserId, currentUserRole, string.Join(",", currentUserGroups));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing user claims during DownloadFile for File ID {FileId}. User: {Identity}", id, User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            // Получаем метаданные
            FileMetadata metadata = null;
            try
            {
                metadata = await _metadataService.GetMetadataByIdAsync(id);
            }
            catch (Exception exMeta)
            {
                _logger.LogError(exMeta, "Error retrieving metadata for File ID {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information.");
            }

            if (metadata == null)
            {
                _logger.LogWarning("File metadata not found for ID {FileId}", id);
                return NotFound("File metadata not found.");
            }
            _logger.LogDebug("Metadata retrieved for File ID {FileId}. Owner ID: {OwnerId}, Group: {OwnerGroup}, Path: {FilePath}", id, metadata.UserId, metadata.UserGroup, metadata.FilePath);


            // --- Проверка прав доступа ---
            bool canDownload = false;
            if (currentUserRole == AppRoles.SuperAdmin) canDownload = true;
            else if (currentUserRole == AppRoles.Admin) canDownload = (metadata.UserId == currentUserId) || (metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup));
            else if (currentUserRole == AppRoles.User) canDownload = (metadata.UserId == currentUserId);

            if (!canDownload)
            {
                _logger.LogWarning("Access DENIED for User ID {UserId} (Role: {UserRole}) to download File ID {FileId} owned by User ID {OwnerId}", currentUserId, currentUserRole, id, metadata.UserId);
                return Forbid("You do not have permission to access this file.");
            }
            _logger.LogInformation("Access GRANTED for User ID {UserId} to download File ID {FileId}", currentUserId, id);
            // --- Конец проверки прав доступа ---


            // --- Проверка пути и файла на диске ---
            if (string.IsNullOrEmpty(metadata.FilePath))
            {
                _logger.LogError("File path is MISSING in metadata for File ID {FileId}", id);
                return NotFound("File path information is missing.");
            }
            _logger.LogInformation("File path from metadata for File ID {FileId} is: {FilePath}", id, metadata.FilePath);
            // --- Конец проверки пути ---


            // Основной try блок для работы с файлом
            FileStream fileStream = null; // Объявляем заранее для доступа в catch/finally
            try
            {
                // Проверку существования переносим внутрь try
                _logger.LogInformation("Checking file existence for path: {FilePath}", metadata.FilePath);
                if (!System.IO.File.Exists(metadata.FilePath))
                {
                    _logger.LogError("File.Exists returned FALSE for path: {FilePath} (File ID: {FileId})", metadata.FilePath, id);
                    return NotFound("File data is missing on the server (checked existence).");
                }
                _logger.LogInformation("File.Exists returned TRUE for path: {FilePath}", metadata.FilePath);


                _logger.LogDebug("Creating FileStream for path: {FilePath}", metadata.FilePath);
                // FileMode.Open, FileAccess.Read, FileShare.Read - для чтения существующего файла
                fileStream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                _logger.LogInformation("FileStream created for {FilePath}. CanRead: {CanRead}, Length: {Length}", metadata.FilePath, fileStream.CanRead, fileStream.Length);

                // Проверка на нулевую длину файла
                if (fileStream.Length == 0)
                {
                    _logger.LogWarning("FileStream Length is 0 for path: {FilePath}. Returning NotFound.", metadata.FilePath);
                    await fileStream.DisposeAsync(); // Закрываем пустой поток
                    fileStream = null; // Обнуляем, чтобы не попасть в finally
                    return NotFound("File appears to be empty on the server.");
                }

                // Устанавливаем Content-Disposition
                var contentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
                contentDisposition.SetHttpFileName(metadata.OriginalName); // Используем безопасный метод
                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
                _logger.LogInformation("Serving file {FileId} ({FileName}) with Content-Disposition: {Disposition}", id, metadata.OriginalName, contentDisposition.DispositionType);

                // Возвращаем файл как поток. FileStreamResult сам позаботится о закрытии потока fileStream.
                // Важно: не используем using для fileStream здесь, т.к. File() должен управлять его временем жизни.
                return File(fileStream, metadata.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
            }
            // Блоки catch для разных ошибок чтения файла
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "FileNotFoundException occurred while trying to open File ID {FileId} at path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync(); // Закрываем, если открылся
                return NotFound("File data is missing on the server (FileNotFoundException).");
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "DirectoryNotFoundException occurred while trying to open File ID {FileId} at path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return NotFound("File directory missing on the server.");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO Error reading file {FileId} for download/preview. Path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Error reading file from storage.");
            }
            catch (UnauthorizedAccessException ex) // Ошибка прав доступа к файлу на сервере
            {
                _logger.LogError(ex, "UnauthorizedAccessException reading file {FileId} for download/preview. Path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Permission error reading file from storage.");
            }
            catch (Exception ex) // Другие непредвиденные ошибки
            {
                _logger.LogError(ex, "An unexpected error occurred while processing file download/preview for File ID {FileId}", id);
                if (fileStream != null) await fileStream.DisposeAsync(); // Закрываем и здесь
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
            // Блок finally здесь не нужен, т.к. FileStreamResult сам закроет поток,
            // а в catch блоках мы закрываем его вручную перед возвратом ошибки.
        }


        // --- Удаление файла ---
        [HttpDelete("files/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")] // Только ВА и Админы могут удалять
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            _logger.LogInformation("Delete request for File ID {FileId}", id);
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;

            // Оборачиваем весь основной блок в try-catch
            try
            {
                // --- Парсим клеймы пользователя ВНУТРИ try ---
                try
                {
                    currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                    currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                    currentUserGroups = User.FindAll("group").Select(c => c.Value).ToList();
                    _logger.LogDebug("Requesting user for delete - ID: {UserId}, Role: {UserRole}", currentUserId, currentUserRole);
                }
                catch (Exception exParse)
                {
                    _logger.LogError(exParse, "Error parsing user claims during DeleteFile for File ID {FileId}. User: {Identity}", id, User.Identity?.Name ?? "N/A");
                    return Unauthorized("Invalid user claims in token.");
                }


                // --- Получаем метаданные ВНУТРИ try ---
                FileMetadata metadata = null;
                try
                {
                    metadata = await _metadataService.GetMetadataByIdAsync(id);
                }
                catch (Exception exMeta)
                {
                    _logger.LogError(exMeta, "Error retrieving metadata for File ID {FileId} during delete operation", id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information for deletion.");
                }

                if (metadata == null)
                {
                    _logger.LogWarning("Delete request for non-existent File ID {FileId}", id);
                    return NotFound("File metadata not found.");
                }
                _logger.LogDebug("Metadata found for deletion. File ID {FileId}, Owner ID: {OwnerId}", id, metadata.UserId);


                // --- Проверка прав на удаление ВНУТРИ try ---
                bool canDelete = false;
                if (currentUserRole == AppRoles.SuperAdmin) canDelete = true;
                else if (currentUserRole == AppRoles.Admin) canDelete = (metadata.UserId == currentUserId) || (metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup));

                if (!canDelete)
                {
                    _logger.LogWarning("Access DENIED for User ID {UserId} to delete File ID {FileId}", currentUserId, id);
                    return Forbid("You do not have permission to delete this file.");
                }
                _logger.LogInformation("Delete access GRANTED for User ID {UserId} to File ID {FileId}", currentUserId, id);
                // --- Конец проверки прав ---


                // --- Удаляем метаданные и файл ВНУТРИ try ---
                var filePath = await _metadataService.DeleteMetadataAndFileAsync(id);

                if (filePath != null)
                {
                    // --- Уведомление SearchService об удалении (асинхронно) ---
                    _ = Task.Run(() => NotifySearchServiceDeleteAsync(id));
                    // -------------------------------------------

                    _logger.LogInformation("Attempting to delete file from disk: {FilePath}", filePath);
                    if (System.IO.File.Exists(filePath))
                    {
                        try
                        {
                            System.IO.File.Delete(filePath);
                            _logger.LogInformation("File deleted successfully from disk: {FilePath} by User ID {UserId}", filePath, currentUserId);
                            // Опционально: Проверить, пуста ли папка пользователя/группы и удалить ее?
                        }
                        // Ловим конкретные ошибки при удалении файла
                        catch (IOException ioEx) { _logger.LogError(ioEx, "IO Error deleting file from disk: {FilePath}. Metadata was removed for {FileId}.", filePath, id); }
                        catch (UnauthorizedAccessException uaEx) { _logger.LogError(uaEx, "Unauthorized access deleting file from disk: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                        catch (Exception fileDelEx) { _logger.LogError(fileDelEx, "Unexpected error deleting file from disk: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                    }
                    else { _logger.LogWarning("File not found on disk during deletion: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                }
                else
                {
                    // Метаданные не найдены (кто-то удалил раньше?)
                    _logger.LogWarning("DeleteMetadataAndFileAsync returned null path for File ID {FileId}, possibly already deleted by another request.", id);
                    return NotFound("File metadata not found (or already deleted).");
                }

                // Если все прошло успешно (или с ошибками удаления файла, но не метаданных)
                return NoContent(); // 204 No Content
            }
            // --- Ловим другие внешние ошибки ---
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during the file deletion process for File ID {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while deleting the file.");
            }
        }


        // --- НОВЫЙ ВНУТРЕННИЙ ЭНДПОИНТ для SearchService ---
        [HttpGet("internal/download/{id}")]
        [AllowAnonymous] // <-- Разрешаем анонимный доступ, но доступ должен ограничиваться сетью Docker
        public async Task<IActionResult> InternalDownloadFile(Guid id)
        {
            // --- !!! ВАЖНО: Добавьте проверку IP или другой механизм,
            // чтобы убедиться, что этот эндпоинт вызывается только из доверенной сети (например, SearchService) !!! ---
            // Пример (упрощенный, зависит от настроек сети):
            // var remoteIp = HttpContext.Connection.RemoteIpAddress;
            // if (remoteIp == null || !IsInternalNetwork(remoteIp)) { // Нужна функция IsInternalNetwork
            //      _logger.LogWarning("Unauthorized access attempt to internal download endpoint from IP: {RemoteIp}", remoteIp);
            //      return Forbid();
            // }
            _logger.LogInformation("Internal download request received for File ID {FileId}", id);

            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(id); }
            catch (Exception exMeta) { _logger.LogError(exMeta, "InternalDownload: Error retrieving metadata for {FileId}", id); return StatusCode(500); }

            if (metadata == null || string.IsNullOrEmpty(metadata.FilePath)) { _logger.LogWarning("InternalDownload: Metadata or path not found for {FileId}", id); return NotFound(); }

            _logger.LogInformation("InternalDownload: Attempting access to {FilePath}", metadata.FilePath);

            if (!System.IO.File.Exists(metadata.FilePath)) { _logger.LogError("InternalDownload: File does not exist at {FilePath}", metadata.FilePath); return NotFound(); }

            try
            {
                // Возвращаем файл как поток, чтобы SearchService мог его обработать
                var fileStream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                _logger.LogInformation("InternalDownload: Serving file {FileId}, Length: {Length}", id, fileStream.Length);
                // Возвращаем как FileStreamResult, он сам закроет поток
                // Указываем ContentType, т.к. SearchService (Tika) может его использовать
                return File(fileStream, metadata.ContentType ?? "application/octet-stream");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InternalDownload: Error serving file {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to serve internal file download.");
            }
        }
        // --- Конец нового эндпоинта ---


        // --- Вспомогательные методы для уведомления SearchService ---
        private async Task NotifySearchServiceIndexAsync(Guid fileId, int userId)
        {
            string searchServiceUrl = "http://searchservice/api/index/index";
            FileMetadata metadata = null; // Получаем метаданные, чтобы передать имя и тип
            try { metadata = await _metadataService.GetMetadataByIdAsync(fileId); } catch { } // Игнорируем ошибку здесь

            if (metadata == null)
            {
                _logger.LogWarning("NotifySearchServiceIndexAsync: Could not retrieve metadata for File ID {FileId}. Skipping notification.", fileId);
                return;
            }

            try
            {
                _logger.LogInformation("Notifying SearchService to index File ID {FileId} (Name: {FileName}, Type: {ContentType}) for User ID {UserId} at {Url}",
                    fileId, metadata.OriginalName, metadata.ContentType, userId, searchServiceUrl);
                using var client = _httpClientFactory.CreateClient("SearchServiceClient");
                client.Timeout = TimeSpan.FromSeconds(30);
                // Передаем новые поля
                var payload = new
                {
                    FileId = fileId,
                    UserId = userId,
                    OriginalName = metadata.OriginalName, // <-- Передаем
                    ContentType = metadata.ContentType   // <-- Передаем
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await client.PostAsync(searchServiceUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SearchService successfully acknowledged indexing request for File ID {FileId}", fileId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(); // Читаем тело ошибки
                    _logger.LogWarning("SearchService indexing request failed for File ID {FileId}. Status: {StatusCode}, Reason: {Reason}, Body: {ErrorBody}",
                       fileId, response.StatusCode, response.ReasonPhrase, errorBody.Substring(0, Math.Min(errorBody.Length, 500))); // Логируем начало тела ошибки
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send indexing request to SearchService ({Url}) for File ID {FileId}", searchServiceUrl, fileId);
                // Здесь не бросаем исключение, чтобы не сломать основной поток загрузки
            }
        }

        private async Task NotifySearchServiceDeleteAsync(Guid fileId)
        {
            string searchServiceUrl = $"http://searchservice/api/index/index/{fileId}"; // URL внутри Docker сети
            try
            {
                _logger.LogInformation("Notifying SearchService to delete index for File ID {FileId} at {Url}", fileId, searchServiceUrl);
                using var client = _httpClientFactory.CreateClient("SearchServiceClient");
                client.Timeout = TimeSpan.FromSeconds(10);

                using var response = await client.DeleteAsync(searchServiceUrl);

                if (response.IsSuccessStatusCode)
                { // NoContent (204) тоже IsSuccessStatusCode
                    _logger.LogInformation("SearchService successfully acknowledged index deletion request for File ID {FileId}", fileId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("SearchService index deletion request failed for File ID {FileId}. Status: {StatusCode}, Reason: {Reason}, Body: {ErrorBody}",
                       fileId, response.StatusCode, response.ReasonPhrase, errorBody.Substring(0, Math.Min(errorBody.Length, 500)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send index deletion request to SearchService ({Url}) for File ID {FileId}", searchServiceUrl, fileId);
            }
        }

    } // --- Конец класса FileController ---

    // Модель для загрузки файла
    public class FileUploadModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
    }
} // --- Конец namespace ---