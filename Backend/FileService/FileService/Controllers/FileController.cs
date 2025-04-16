using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using FileService.Models;
using FileService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;


namespace FileService.Controllers
{
    // Роли должны совпадать с AuthService!
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
        private readonly string _uploadBasePath;

        public FileController(IFileMetadataService metadataService, ILogger<FileController> logger, IConfiguration configuration)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _uploadBasePath = configuration["UploadBasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            _logger.LogInformation("File upload base path configured to: {UploadPath}", _uploadBasePath);

            try
            {
                if (!Directory.Exists(_uploadBasePath))
                {
                    Directory.CreateDirectory(_uploadBasePath);
                    _logger.LogInformation("Created base upload directory: {UploadPath}", _uploadBasePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to create or access base upload directory: {UploadPath}. File uploads might fail.", _uploadBasePath);
            }
        }

        // --- Загрузка файла ---
        [HttpPost("upload")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            // Проверяем наличие файла и его размер
            if (model?.File == null || model.File.Length == 0)
            {
                _logger.LogWarning("UploadFile attempt with no file or zero length.");
                return BadRequest("No file uploaded or file is empty."); // 400 Bad Request
            }
            // Проверка максимального размера (найти как сделать через конфиг)
            // if (model.File.Length > 100 * 1024 * 1024) // 100 MB limit example
            // {
            //     _logger.LogWarning("UploadFile rejected: File size {FileSize} exceeds limit.", model.File.Length);
            //     return BadRequest("File size exceeds the allowed limit.");
            // }


            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userPrimaryGroup = User.FindAll("group").FirstOrDefault()?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(userPrimaryGroup))
            {
                userPrimaryGroup = "Default";
                _logger.LogInformation("User ID {UserId} has no groups in token, using 'Default' group for upload path.", userId);
            }

            _logger.LogInformation("UploadFile request by UserID: {UserId}, Primary Group: {UserGroup}, Original Filename: {FileName}, Size: {FileSize} bytes",
                userId, userPrimaryGroup, model.File.FileName, model.File.Length);

            var sanitizedGroupName = SanitizeFolderName(userPrimaryGroup);
            var userDirectoryPath = Path.Combine(_uploadBasePath, sanitizedGroupName, userId.ToString());

            try
            {
                if (!Directory.Exists(userDirectoryPath))
                {
                    Directory.CreateDirectory(userDirectoryPath);
                    _logger.LogInformation("Created user upload directory: {UserDirPath}", userDirectoryPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user upload directory: {UserDirPath} for UserID: {UserId}", userDirectoryPath, userId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error: Could not prepare storage location.");
            }

            var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(model.File.FileName)}";
            var filePath = Path.Combine(userDirectoryPath, storedFileName);

            try
            {
                _logger.LogDebug("Attempting to save uploaded file to: {FilePath}", filePath);
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await model.File.CopyToAsync(stream, HttpContext.RequestAborted);
                }
                _logger.LogInformation("Successfully saved uploaded file to: {FilePath}", filePath);

                var metadata = new FileMetadata
                {
                    Id = Guid.NewGuid(),
                    OriginalName = model.File.FileName,
                    StoredFileName = storedFileName,
                    ContentType = model.File.ContentType,
                    SizeBytes = model.File.Length,
                    UploadedAt = DateTime.UtcNow,
                    UserId = userId,
                    UserGroup = userPrimaryGroup,
                    FilePath = filePath
                };
                await _metadataService.AddMetadataAsync(metadata);
                _logger.LogInformation("Metadata saved for file ID: {FileId}, Original Name: {OriginalName}", metadata.Id, metadata.OriginalName);

                // Возвращаем 201 Created или 200 OK
                return Ok(new { FileId = metadata.Id, FileName = metadata.OriginalName, Group = metadata.UserGroup });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("File upload cancelled by client for UserID: {UserId}, Original Name: {FileName}", userId, model.File.FileName);
                TryDeleteFile(filePath);
                return StatusCode(StatusCodes.Status499ClientClosedRequest, "Client closed request during file upload.");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error saving uploaded file {FilePath} for UserID: {UserId}", filePath, userId);
                TryDeleteFile(filePath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error: Could not save file data.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error during file upload process for UserID: {UserId}, Original Name: {FileName}", userId, model.File.FileName);
                TryDeleteFile(filePath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error during file upload.");
            }
        }

        // --- Получение списка файлов ---
        [HttpGet("files")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> GetFiles()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var userGroups = User.FindAll("group").Select(c => c.Value.Trim()).Where(g => !string.IsNullOrEmpty(g)).ToList();

            _logger.LogInformation("GetFiles request by UserID: {UserId}, Role: {UserRole}", userId, userRole);

            IEnumerable<FileMetadata> files;
            object result;

            try
            {
                if (userRole == AppRoles.SuperAdmin)
                {
                    files = await _metadataService.GetAllMetadataAsync();
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt }).ToList();
                    _logger.LogInformation("SuperAdmin ({UserId}) retrieved {FileCount} total files.", userId, (result as List<dynamic>)?.Count ?? 0);
                }
                else if (userRole == AppRoles.Admin)
                {
                    if (!userGroups.Any())
                    {
                        _logger.LogInformation("Admin ({UserId}) has no groups, retrieving own files only.", userId);
                        files = await _metadataService.GetMetadataByUserAsync(userId);
                    }
                    else
                    {
                        _logger.LogInformation("Admin ({UserId}) retrieving files for groups [{ManagedGroups}] and own files.", userId, string.Join(", ", userGroups));
                        files = await _metadataService.GetMetadataByUserGroupsAsync(userGroups, userId);
                    }
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt }).ToList();
                    _logger.LogInformation("Admin ({UserId}) retrieved {FileCount} files.", userId, (result as List<dynamic>)?.Count ?? 0);
                }
                else // UserRole == AppRoles.User
                {
                    files = await _metadataService.GetMetadataByUserAsync(userId);
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.SizeBytes, f.UploadedAt }).ToList();
                    _logger.LogInformation("User ({UserId}) retrieved {FileCount} own files.", userId, (result as List<dynamic>)?.Count ?? 0);
                }

                // --- Логирование перед возвратом ---
                var resultList = result as System.Collections.IList;
                var resultType = result?.GetType().FullName ?? "null";
                _logger.LogInformation("--- FileService GetFiles: Returning Ok. Result Type: {ResultType}, Count: {ResultCount}", resultType, resultList?.Count ?? -1);
                if (resultList != null && resultList.Count > 0)
                {
                    try
                    {
                        var firstElementJson = JsonSerializer.Serialize(resultList[0]);
                        _logger.LogInformation("--- FileService GetFiles: First element sample: {FirstElementJson}", firstElementJson);
                    }
                    catch (Exception jsonEx)
                    {
                        _logger.LogWarning(jsonEx, "Could not serialize first element for logging.");
                    }
                }
                else if (resultList != null && resultList.Count == 0)
                {
                    _logger.LogInformation("--- FileService GetFiles: Result list is empty.");
                }
                else
                {
                    _logger.LogWarning("--- FileService GetFiles: Result is not a list or is null.");
                    if (result != null)
                        return StatusCode(500, "Internal error: File list data format is incorrect.");
                }

                return Ok(result ?? new List<object>()); // 200 OK
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file list for UserID: {UserId}, Role: {UserRole}", userId, userRole);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred while retrieving files.");
            }
        }


        // --- Скачивание файла ---
        [HttpGet("download/{id:guid}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DownloadFile(Guid id)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
            _logger.LogInformation("DownloadFile request for ID: {FileId} by UserID: {UserId}, Role: {UserRole}", id, currentUserId, currentUserRole);

            FileMetadata metadata;
            try
            {
                metadata = await _metadataService.GetMetadataByIdAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving metadata for File ID: {FileId} during download request by UserID: {UserId}", id, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information.");
            }

            if (metadata == null)
            {
                _logger.LogWarning("DownloadFile failed: Metadata not found for ID: {FileId}. Request by UserID: {UserId}", id, currentUserId);
                return NotFound("File metadata not found."); // 404 Not Found
            }

            // Проверка прав доступа
            bool canDownload = CheckDownloadPermissions(metadata, currentUserId, currentUserRole);

            if (!canDownload)
            {
                _logger.LogWarning("Forbidden download attempt for File ID: {FileId} (Owner: {OwnerId}) by UserID: {RequesterId}, Role: {RequesterRole}",
                    id, metadata.UserId, currentUserId, currentUserRole);
                return Forbid("You do not have permission to download this file."); // 403 Forbidden
            }

            // Проверяем существование файла на диске
            if (string.IsNullOrEmpty(metadata.FilePath) || !System.IO.File.Exists(metadata.FilePath))
            {
                _logger.LogError("DownloadFile failed: File data missing on server for File ID: {FileId}. Expected path: {FilePath}. Request by UserID: {UserId}", id, metadata.FilePath ?? "N/A", currentUserId);
                return NotFound("File data is missing on the server."); // 404 Not Found
            }

            try
            {
                _logger.LogDebug("Streaming file: {FilePath} for download (File ID: {FileId}).", metadata.FilePath, id);
                var fileStream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                return File(fileStream, metadata.ContentType ?? "application/octet-stream", metadata.OriginalName); // 200 OK с файлом
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "DownloadFile error: File disappeared between check and open for File ID: {FileId}, Path: {FilePath}", id, metadata.FilePath);
                return NotFound("File data disappeared unexpectedly."); // 404 Not Found
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error reading file for download: {FilePath}", metadata.FilePath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error reading file data."); // 500 Internal Server Error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error during file download for File ID: {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred while processing the file download."); // 500 Internal Server Error
            }
        }

        // --- Удаление файла ---
        [HttpDelete("files/{id:guid}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")] // Роли, которые могут
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
            _logger.LogInformation("DeleteFile request for ID: {FileId} by UserID: {UserId}, Role: {UserRole}", id, currentUserId, currentUserRole);

            FileMetadata metadata;
            try
            {
                metadata = await _metadataService.GetMetadataByIdAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving metadata for File ID: {FileId} during delete request by UserID: {UserId}", id, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information for deletion.");
            }


            if (metadata == null)
            {
                _logger.LogWarning("DeleteFile: Metadata not found for ID: {FileId}. Request by UserID: {UserId}. Returning NotFound.", id, currentUserId);
                return NotFound("File metadata not found."); // 404 Not Found
            }

            // Проверка прав доступа на удаление
            bool canDelete = CheckDeletePermissions(metadata, currentUserId, currentUserRole);

            if (!canDelete)
            {
                _logger.LogWarning("Forbidden delete attempt for File ID: {FileId} (Owner: {OwnerId}) by UserID: {RequesterId}, Role: {RequesterRole}",
                    id, metadata.UserId, currentUserId, currentUserRole);
                return Forbid("You do not have permission to delete this file."); // 403 Forbidden
            }

            try
            {
                // Удаляем метаданные и получаем путь к файлу
                var filePath = await _metadataService.DeleteMetadataAndFileAsync(id);

                if (filePath != null)
                {
                    TryDeleteFile(filePath); // Используем хелпер для удаления файла с диска
                }
                else
                {
                    _logger.LogInformation("DeleteFile: Metadata for ID {FileId} was already deleted (or delete failed silently).", id);
                }

                _logger.LogInformation("Successfully processed delete request for File ID: {FileId} by UserID: {UserId}", id, currentUserId);
                return NoContent(); // 204 No Content
            }
            catch (Exception ex) // Ловим ошибки при удалении метаданных или файла
            {
                _logger.LogError(ex, "Error during delete process for File ID: {FileId}. Request by UserID: {UserId}", id, currentUserId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while deleting the file."); // 500 Internal Server Error
            }
        }


        // --- Вспомогательные методы ---

        private bool CheckDownloadPermissions(FileMetadata metadata, int currentUserId, string currentUserRole)
        {
            if (currentUserRole == AppRoles.SuperAdmin) return true; // ВА может все
            if (metadata.UserId == currentUserId) return true; // Владелец может скачать свой файл

            if (currentUserRole == AppRoles.Admin)
            {
                // Админ может скачать файл пользователя из его групп
                var currentUserGroups = User.FindAll("group")
                                            .Select(c => c.Value?.Trim())
                                            .Where(g => !string.IsNullOrEmpty(g))
                                            .ToList();
                // Проверяем, что у админа есть группы и группа файла не пустая и входит в группы админа
                bool isFileInManagedGroup = currentUserGroups.Any()
                                           && !string.IsNullOrEmpty(metadata.UserGroup)
                                           && currentUserGroups.Contains(metadata.UserGroup);
                return isFileInManagedGroup;
            }

            return false; // Обычный пользователь не может скачать чужой файл
        }

        private bool CheckDeletePermissions(FileMetadata metadata, int currentUserId, string currentUserRole)
        {
            // Пользователи (User) не могут удалять файлы через этот endpoint
            if (currentUserRole == AppRoles.User) return false;

            // Для Admin и SuperAdmin логика удаления совпадает с логикой скачивания в данном сценарии
            return CheckDownloadPermissions(metadata, currentUserId, currentUserRole);
        }


        private static readonly char[] _invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_invalid_";

            // Заменяем недопустимые символы на подчеркивание
            string sanitized = new string(name.Select(ch => _invalidChars.Contains(ch) ? '_' : ch).ToArray());

            // Убираем точки и пробелы в начале/конце
            sanitized = sanitized.Trim('.', ' ');

            // Заменяем множественные подчеркивания на одно
            while (sanitized.Contains("__")) sanitized = sanitized.Replace("__", "_");

            // Если имя стало пустым после очистки
            if (string.IsNullOrWhiteSpace(sanitized)) return "_empty_";

            // Ограничение длины (опционально)
            const int MaxLength = 100;
            if (sanitized.Length > MaxLength) sanitized = sanitized.Substring(0, MaxLength);

            _logger.LogDebug("Sanitized folder name: '{OriginalName}' -> '{SanitizedName}'", name, sanitized);
            return sanitized;
        }

        private void TryDeleteFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogInformation("Successfully deleted physical file: {FilePath}", filePath);
                }
                else
                {
                    _logger.LogWarning("Attempted to delete file, but it does not exist: {FilePath}", filePath);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IO error deleting file: {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access trying to delete file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error trying to delete file: {FilePath}", filePath);
            }
        }
    }
    public class FileUploadModel
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "File is required.")]
        public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
    }
}