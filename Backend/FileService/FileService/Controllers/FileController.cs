using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
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
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FileService.Controllers
{
    public static class AppRoles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string User = "User";
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FileController : ControllerBase
    {
        private readonly IFileMetadataService _metadataService;
        private readonly ILogger<FileController> _logger;
        private readonly string _uploadBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        private readonly IHttpClientFactory _httpClientFactory;

        public FileController(
            IFileMetadataService metadataService,
            ILogger<FileController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            try
            {
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
            }
        }

        internal const string GroupClaimTypeUri = "http://schemas.xmlsoap.org/claims/Group";

        [HttpPut("files/{id}/group")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")] // Разрешаем всем авторизованным
        public async Task<IActionResult> UpdateFileGroup(Guid id, [FromBody] UpdateFileGroupRequest model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group";
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "";
                currentUserGroups = User.FindAll(groupClaimType).Select(c => c.Value).ToList();
                if (string.IsNullOrEmpty(currentUserRole)) throw new Exception("User role claim is missing.");
                //_logger.LogInformation("UpdateFileGroup request for File ID {FileId} to Group '{NewGroup}' by User ID {UserId}, Role: {UserRole}",
                //    id, model.NewGroup, currentUserId, currentUserRole);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Unexpected error updating group for File ID {FileId}", id);
                return Unauthorized("Invalid user claims in token.");
            }
            try
            {
                bool success = await _metadataService.UpdateFileGroupAsync(id, model.NewGroup, currentUserId, currentUserRole, currentUserGroups);

                if (success)
                {
                    return NoContent();
                }
                else
                {
                    var metadataExists = await _metadataService.GetMetadataByIdAsync(id) != null;
                    if (!metadataExists)
                    {
                        //_logger.LogWarning("UpdateFileGroup failed: File ID {FileId} not found.", id);
                        return NotFound("File metadata not found.");
                    }
                    else
                    {
                        //_logger.LogWarning("UpdateFileGroup failed: Insufficient permissions or invalid target group for User ID {UserId} on File ID {FileId} trying to set group '{NewGroup}'.", currentUserId, id, model.NewGroup);
                        return Forbid("Insufficient permissions or invalid target group specified.");
                    }
                }
            }catch (Exception ex) { return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while updating groups."); }
        }
        public class UpdateFileGroupRequest
        {
            [Required(ErrorMessage = "Необходимо указать новую группу.")]
            [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "Имя группы может содержать только буквы, цифры, _, -, .")]
            public string NewGroup { get; set; } = string.Empty;
        }

        [HttpPost("upload")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            if (!ModelState.IsValid) // Проверяем валидность модели
            {
                _logger.LogWarning("UploadFile validation failed. User: {Identity}", User.Identity?.Name ?? "N/A");
                return BadRequest(ModelState);
            }
            // Ограничение размера файла (пример: 100MB)
            long maxFileSize = 100 * 1024 * 1024;
            if (model.File.Length > maxFileSize)
            {
                //_logger.LogWarning("UploadFile attempt by User (ClaimsPrincipal Identity: {Identity}) with file size {FileSize} exceeding limit {Limit}.", User.Identity?.Name ?? "N/A", model.File.Length, maxFileSize);
                return BadRequest($"File size exceeds the {maxFileSize / 1024 / 1024} MB limit.");
            }

            int userId;
            List<string> userGroups;
            string? userRoleClaimValue;
            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRoleClaimValue = User.FindFirstValue(ClaimTypes.Role);

                // --- Логируем ВСЕ клеймы пользователя ---
                var allClaims = User.Claims.Select(c => $"Type={c.Type}, Value={c.Value}").ToList();
                _logger.LogInformation("UploadFile - ALL User Claims ({ClaimCount}): [{ClaimsList}]", allClaims.Count, string.Join(" | ", allClaims));
                // ----------------------------------------

                // Теперь попробуем найти группы снова, используя ТОЧНЫЙ тип ClaimTypes.Role
                const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group"; // <-- Используем тип из лога
                userGroups = User.FindAll(groupClaimType)
                                             .Select(c => c.Value)
                                             .ToList();

                _logger.LogInformation("UploadFile - User Claims: ID={UserId}, MainRole={UserRole}, Found Group Claims (Type='{GroupClaimType}'): [{UserGroups}]",
                userId, userRoleClaimValue ?? "N/A", groupClaimType, string.Join(",", userGroups));

                if (userGroups == null || !userGroups.Any())
                {
                    _logger.LogWarning("Upload attempt by User ID {UserId} failed: User has no group claims with type '{GroupClaimType}'.", userId, groupClaimType);
                    return StatusCode(StatusCodes.Status403Forbidden, "You must belong to at least one group to upload files.");
                }

                // Проверка принадлежности к targetGroup
                if (!userGroups.Contains(model.TargetGroup))
                {
                    _logger.LogWarning("Upload attempt by User ID {UserId} failed: User does not belong to the target group '{TargetGroup}'. User groups: [{UserGroups}]", userId, model.TargetGroup, string.Join(",", userGroups));
                    return StatusCode(StatusCodes.Status403Forbidden, $"You do not have permission to upload to group '{model.TargetGroup}'.");
                }
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userGroups = User.FindAll(GroupClaimTypeUri) // <-- Используем константу/литерал
                                             .Select(c => c.Value)
                                             .ToList(); var userRoleClaim = User.FindFirstValue(ClaimTypes.Role); // Получаем роль для лога
                _logger.LogInformation("UploadFile - User Claims: ID={UserId}, Role={UserRole}, Found Group Claims (Type='{GroupClaimTypeUri}'): [{UserGroups}]",userId, userRoleClaimValue ?? "N/A", GroupClaimTypeUri, string.Join(",", userGroups));
                //_logger.LogInformation("UploadFile request by User ID {UserId}, Primary Group: {Group}", userId, userPrimaryGroup);
                if (userGroups == null || !userGroups.Any())
                {
                    _logger.LogWarning("Upload attempt by User ID {UserId} failed: User has no group claims with type '{GroupClaimTypeUri}'.", userId, GroupClaimTypeUri);
                    return StatusCode(StatusCodes.Status403Forbidden, "You must belong to at least one group to upload files.");
                }
                //_logger.LogInformation("UploadFile request by User ID {UserId}, Target Group: {TargetGroup}", userId, model.TargetGroup);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error parsing user claims during file upload. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }
            _logger.LogInformation("Checking TargetGroup: Model.TargetGroup='{TargetGroup}', UserGroups=[{UserGroups}]", model.TargetGroup, string.Join(",", userGroups));
            // Проверка: пользователь должен состоять в группе, которую он выбрал
            if (!userGroups.Contains(model.TargetGroup))
            {
                _logger.LogWarning("Upload attempt by User ID {UserId} failed: User does not belong to the target group '{TargetGroup}'.", userId, model.TargetGroup);
                return Forbid($"You do not have permission to upload to group '{model.TargetGroup}'.");
            }

            // Используем ID пользователя для директории, чтобы избежать конфликтов имен групп
            string userDirectoryPath = Path.Combine(_uploadBasePath, userId.ToString());
            try
            {
                Directory.CreateDirectory(userDirectoryPath); // Создаем директорию, если её нет
                //_logger.LogDebug("Ensured user directory exists: {Path}", userDirectoryPath);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Failed to create user directory for upload: {Path}", userDirectoryPath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error creating user directory.");
            }

            var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(model.File.FileName)}";
            var filePath = Path.Combine(userDirectoryPath, storedFileName);
            //_logger.LogInformation("Generated target file path: {FilePath}", filePath);

            FileMetadata metadata = null;
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.File.CopyToAsync(stream);
                }
                //_logger.LogInformation("File content saved successfully to {FilePath} by User ID {UserId}", filePath, userId);

                metadata = new FileMetadata
                {
                    Id = Guid.NewGuid(),
                    OriginalName = model.File.FileName,
                    StoredFileName = storedFileName,
                    ContentType = model.File.ContentType,
                    SizeBytes = model.File.Length,
                    UploadedAt = DateTime.UtcNow,
                    UserId = userId,
                    UserGroup = model.TargetGroup,
                    FilePath = filePath
                };
                await _metadataService.AddMetadataAsync(metadata);
                //_logger.LogInformation("Metadata saved for file {FileId}", metadata.Id);

                if (metadata != null && metadata.Id != Guid.Empty)
                {
                    _ = Task.Run(() => NotifySearchServiceIndexAsync(metadata.Id, metadata.UserId));
                }

                return Ok(new { FileId = metadata.Id, FileName = metadata.OriginalName, Group = metadata.UserGroup });
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error during file upload processing for User ID {UserId}, Target Path: {FilePath}", userId, filePath);
                if (System.IO.File.Exists(filePath))
                {
                    try { System.IO.File.Delete(filePath); _logger.LogWarning("Partial file deleted due to error: {FilePath}", filePath); }
                    catch (Exception delEx) { _logger.LogError(delEx, "Failed to delete partial file after error: {FilePath}", filePath); }
                }
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error during file upload.");
            }
        }

        [HttpGet("files")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> GetFiles([FromQuery] string scope = "default")
        {
            int userId;
            string? userRole;
            List<string> userGroups;
            const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group"; // Или "group", если вернул в AuthService

            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRole = User.FindFirstValue(ClaimTypes.Role);
                userGroups = User.FindAll(groupClaimType).Select(c => c.Value).ToList();
                _logger.LogInformation("GetFiles request by User ID {UserId}, Role {UserRole}, Groups=[{Groups}], Scope={Scope}", userId, userRole ?? "N/A", string.Join(",", userGroups), scope);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing user claims during GetFiles. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            IEnumerable<FileMetadata> files = Enumerable.Empty<FileMetadata>(); // Инициализируем пустым списком

            try
            {
                // 1. Определяем, какие файлы нужно получить из сервиса метаданных
                if (userRole == AppRoles.SuperAdmin)
                {
                    // SuperAdmin всегда видит все файлы
                    files = await _metadataService.GetAllMetadataAsync();
                }
                else if (userRole == AppRoles.Admin)
                {
                    if (scope.Equals("all", StringComparison.OrdinalIgnoreCase)) // Админ на странице "Обзор файлов"
                    {
                        // Админ видит свои файлы И файлы из своих групп
                        files = await _metadataService.GetMetadataByUserGroupsAsync(userGroups, userId);
                    }
                    else if (scope.Equals("group", StringComparison.OrdinalIgnoreCase)) // Админ на странице "Файлы группы" (если будет)
                    {
                        // Админ видит файлы группы (не свои)
                        files = await _metadataService.GetGroupFilesForUserAsync(userId, userGroups);
                    }
                    else // Admin на странице "Мои файлы" (scope="default")
                    {
                        // Админ видит ТОЛЬКО свои файлы
                        files = await _metadataService.GetMetadataByUserAsync(userId);
                    }
                }
                else // User
                {
                    if (scope.Equals("group", StringComparison.OrdinalIgnoreCase)) // Пользователь на странице "Файлы группы"
                    {
                        // Пользователь видит файлы группы (не свои)
                        files = await _metadataService.GetGroupFilesForUserAsync(userId, userGroups);
                    }
                    else // Пользователь на странице "Мои файлы" (scope="default")
                    {
                        // Пользователь видит ТОЛЬКО свои файлы
                        files = await _metadataService.GetMetadataByUserAsync(userId);
                    }
                }

                IEnumerable<object> result;
                result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });


                _logger.LogInformation("Returning {FileCount} files for User ID {UserId} with scope '{Scope}'", files.Count(), userId, scope);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving files for User ID {UserId}, Scope {Scope}", userId, scope);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving files.");
            }
        }

        [HttpGet("search")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> SearchFiles([FromQuery] string term, [FromQuery] string scope = "default")
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                //_logger.LogInformation("Search request with empty term.");
                return Ok(Enumerable.Empty<object>());
            }

            int userId;
            string userRole;
            List<string> userGroups;
            const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group";
            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRole = User.FindFirstValue(ClaimTypes.Role);
                userGroups = User.FindAll(groupClaimType).Select(c => c.Value).ToList();
                //_logger.LogInformation("SearchFiles request by User ID {UserId}, Role {UserRole}, Term: '{SearchTerm}'", userId, userRole, term);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error parsing user claims during SearchFiles. User: {Identity}", User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            try
            {
                IEnumerable<object> result;
                var files = await _metadataService.SearchMetadataAsync(term, userId, userRole, userGroups);
                if (userRole == AppRoles.User && scope.Equals("group", StringComparison.OrdinalIgnoreCase))
                {
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                else if (userRole == AppRoles.SuperAdmin || userRole == AppRoles.Admin)
                {
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.UserId, f.SizeBytes, f.UploadedAt, f.ContentType });
                }
                else
                {
                    result = files.Select(f => new { f.Id, f.OriginalName, f.UserGroup, f.SizeBytes, f.UploadedAt, f.ContentType });
                }

                //_logger.LogInformation("Search returned {FileCount} files for User ID {UserId}", files.Count(), userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error searching files for User ID {UserId}, Term: '{SearchTerm}'", userId, term);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while searching files.");
            }
        }

        [HttpGet("download/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DownloadFile(Guid id, [FromQuery] bool inline = false)
        {
            _logger.LogInformation("Download/Preview request for File ID {FileId}, Inline: {IsInline}", id, inline);
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group";
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                currentUserGroups = User.FindAll(groupClaimType).Select(c => c.Value).ToList();
                //_logger.LogDebug("Requesting user details - ID: {UserId}, Role: {UserRole}, Groups: [{Groups}]", currentUserId, currentUserRole, string.Join(",", currentUserGroups));
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error parsing user claims during DownloadFile for File ID {FileId}. User: {Identity}", id, User.Identity?.Name ?? "N/A");
                return Unauthorized("Invalid user claims in token.");
            }

            FileMetadata metadata = null;
            try
            {
                metadata = await _metadataService.GetMetadataByIdAsync(id);
            }
            catch (Exception exMeta)
            {
                //_logger.LogError(exMeta, "Error retrieving metadata for File ID {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information.");
            }

            if (metadata == null)
            {
                //_logger.LogWarning("File metadata not found for ID {FileId}", id);
                return NotFound("File metadata not found.");
            }
            //_logger.LogDebug("Metadata retrieved for File ID {FileId}. Owner ID: {OwnerId}, Group: {OwnerGroup}, Path: {FilePath}", id, metadata.UserId, metadata.UserGroup, metadata.FilePath);


            bool canAccess = false;
            if (currentUserRole == AppRoles.SuperAdmin) canAccess = true;
            else if (currentUserRole == AppRoles.Admin)
            {
                canAccess = (metadata.UserId == currentUserId) || (metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup));
            }
            else if (currentUserRole == AppRoles.User) canAccess = (metadata.UserId == currentUserId);

            if (!canAccess)
            {
                //_logger.LogWarning("Access DENIED for User ID {UserId} (Role: {UserRole}) to download File ID {FileId} owned by User ID {OwnerId}", currentUserId, currentUserRole, id, metadata.UserId);
                return Forbid("You do not have permission to access this file.");
            }
            //_logger.LogInformation("Access GRANTED for User ID {UserId} to download File ID {FileId}", currentUserId, id);


            if (string.IsNullOrEmpty(metadata.FilePath))
            {
                //_logger.LogError("File path is MISSING in metadata for File ID {FileId}", id);
                return NotFound("File path information is missing.");
            }
            //_logger.LogInformation("File path from metadata for File ID {FileId} is: {FilePath}", id, metadata.FilePath);


            FileStream fileStream = null;
            try
            {
                //_logger.LogInformation("Checking file existence for path: {FilePath}", metadata.FilePath);
                if (!System.IO.File.Exists(metadata.FilePath))
                {
                    //_logger.LogError("File.Exists returned FALSE for path: {FilePath} (File ID: {FileId})", metadata.FilePath, id);
                    return NotFound("File data is missing on the server (checked existence).");
                }
                //_logger.LogInformation("File.Exists returned TRUE for path: {FilePath}", metadata.FilePath);


                //_logger.LogDebug("Creating FileStream for path: {FilePath}", metadata.FilePath);
                // FileMode.Open, FileAccess.Read, FileShare.Read - для чтения существующего файла
                fileStream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                //_logger.LogInformation("FileStream created for {FilePath}. CanRead: {CanRead}, Length: {Length}", metadata.FilePath, fileStream.CanRead, fileStream.Length);

                if (fileStream.Length == 0)
                {
                    //_logger.LogWarning("FileStream Length is 0 for path: {FilePath}. Returning NotFound.", metadata.FilePath);
                    await fileStream.DisposeAsync();
                    fileStream = null;
                    return NotFound("File appears to be empty on the server.");
                }

                var contentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
                contentDisposition.SetHttpFileName(metadata.OriginalName);
                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
                //_logger.LogInformation("Serving file {FileId} ({FileName}) with Content-Disposition: {Disposition}", id, metadata.OriginalName, contentDisposition.DispositionType);

                return File(fileStream, metadata.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
            }
            catch (FileNotFoundException ex)
            {
                //_logger.LogError(ex, "FileNotFoundException occurred while trying to open File ID {FileId} at path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return NotFound("File data is missing on the server (FileNotFoundException).");
            }
            catch (DirectoryNotFoundException ex)
            {
                //_logger.LogError(ex, "DirectoryNotFoundException occurred while trying to open File ID {FileId} at path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return NotFound("File directory missing on the server.");
            }
            catch (IOException ex)
            {
                //_logger.LogError(ex, "IO Error reading file {FileId} for download/preview. Path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Error reading file from storage.");
            }
            catch (UnauthorizedAccessException ex)
            {
                //_logger.LogError(ex, "UnauthorizedAccessException reading file {FileId} for download/preview. Path: {FilePath}", id, metadata.FilePath);
                if (fileStream != null) await fileStream.DisposeAsync();
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Permission error reading file from storage.");
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "An unexpected error occurred while processing file download/preview for File ID {FileId}", id);
                if (fileStream != null) await fileStream.DisposeAsync(); // Закрываем и здесь
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }


        [HttpDelete("files/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            _logger.LogInformation("Delete request for File ID {FileId}", id);
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            const string groupClaimType = "http://schemas.xmlsoap.org/claims/Group";
            try
            {
                try
                {
                    currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                    currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                    currentUserGroups = User.FindAll(groupClaimType).Select(c => c.Value).ToList();
                    //_logger.LogDebug("Requesting user for delete - ID: {UserId}, Role: {UserRole}", currentUserId, currentUserRole);
                }
                catch (Exception exParse)
                {
                    //_logger.LogError(exParse, "Error parsing user claims during DeleteFile for File ID {FileId}. User: {Identity}", id, User.Identity?.Name ?? "N/A");
                    return Unauthorized("Invalid user claims in token.");
                }


                FileMetadata metadata = null;
                try
                {
                    metadata = await _metadataService.GetMetadataByIdAsync(id);
                }
                catch (Exception exMeta)
                {
                    //_logger.LogError(exMeta, "Error retrieving metadata for File ID {FileId} during delete operation", id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information for deletion.");
                }

                if (metadata == null)
                {
                    //_logger.LogWarning("Delete request for non-existent File ID {FileId}", id);
                    return NotFound("File metadata not found.");
                }
                //_logger.LogDebug("Metadata found for deletion. File ID {FileId}, Owner ID: {OwnerId}", id, metadata.UserId);


                bool canDelete = false;
                if (currentUserRole == AppRoles.SuperAdmin) canDelete = true;
                else if (currentUserRole == AppRoles.Admin)
                {
                    canDelete = (metadata.UserId == currentUserId) || (metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup));
                }
                else
                {
                    canDelete = (metadata.UserId == currentUserId);
                }

                if (!canDelete)
                {
                    //_logger.LogWarning("Access DENIED for User ID {UserId} to delete File ID {FileId}", currentUserId, id);
                    return Forbid("You do not have permission to delete this file.");
                }
                //_logger.LogInformation("Delete access GRANTED for User ID {UserId} to File ID {FileId}", currentUserId, id);


                var filePath = await _metadataService.DeleteMetadataAndFileAsync(id);

                if (filePath != null)
                {
                    _ = Task.Run(() => NotifySearchServiceDeleteAsync(id));

                    //_logger.LogInformation("Attempting to delete file from disk: {FilePath}", filePath);
                    if (System.IO.File.Exists(filePath))
                    {
                        try
                        {
                            System.IO.File.Delete(filePath);
                            //_logger.LogInformation("File deleted successfully from disk: {FilePath} by User ID {UserId}", filePath, currentUserId);
                        }
                        catch (IOException ioEx) { _logger.LogError(ioEx, "IO Error deleting file from disk: {FilePath}. Metadata was removed for {FileId}.", filePath, id); }
                        catch (UnauthorizedAccessException uaEx) { _logger.LogError(uaEx, "Unauthorized access deleting file from disk: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                        catch (Exception fileDelEx) { _logger.LogError(fileDelEx, "Unexpected error deleting file from disk: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                    }
                    else { _logger.LogWarning("File not found on disk during deletion: {FilePath}. Metadata removed for {FileId}.", filePath, id); }
                }
                else
                {
                    //_logger.LogWarning("DeleteMetadataAndFileAsync returned null path for File ID {FileId}, possibly already deleted by another request.", id);
                    return NotFound("File metadata not found (or already deleted).");
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "An unexpected error occurred during the file deletion process for File ID {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while deleting the file.");
            }
        }


        [HttpGet("internal/download/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> InternalDownloadFile(Guid id)
        {
            //_logger.LogInformation("Internal download request received for File ID {FileId}", id);

            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(id); }
            catch (Exception exMeta) { _logger.LogError(exMeta, "InternalDownload: Error retrieving metadata for {FileId}", id); return StatusCode(500); }

            if (metadata == null || string.IsNullOrEmpty(metadata.FilePath)) { _logger.LogWarning("InternalDownload: Metadata or path not found for {FileId}", id); return NotFound(); }

            _logger.LogInformation("InternalDownload: Attempting access to {FilePath}", metadata.FilePath);

            if (!System.IO.File.Exists(metadata.FilePath)) { _logger.LogError("InternalDownload: File does not exist at {FilePath}", metadata.FilePath); return NotFound(); }

            try
            {
                var fileStream = new FileStream(metadata.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                //_logger.LogInformation("InternalDownload: Serving file {FileId}, Length: {Length}", id, fileStream.Length);
                return File(fileStream, metadata.ContentType ?? "application/octet-stream");
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "InternalDownload: Error serving file {FileId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to serve internal file download.");
            }
        }

        private async Task NotifySearchServiceIndexAsync(Guid fileId, int userId)
        {
            string searchServiceUrl = "http://searchservice/api/index/index";
            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(fileId); } catch { }

            if (metadata == null)
            {
                //_logger.LogWarning("NotifySearchServiceIndexAsync: Could not retrieve metadata for File ID {FileId}. Skipping notification.", fileId);
                return;
            }

            try
            {
                //_logger.LogInformation("Notifying SearchService to index File ID {FileId} (Name: {FileName}, Type: {ContentType}) for User ID {UserId} at {Url}",
                    //fileId, metadata.OriginalName, metadata.ContentType, userId, searchServiceUrl);
                using var client = _httpClientFactory.CreateClient("SearchServiceClient");
                client.Timeout = TimeSpan.FromSeconds(30);
                var payload = new
                {
                    FileId = fileId,
                    UserId = userId,
                    OriginalName = metadata.OriginalName,
                    ContentType = metadata.ContentType
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await client.PostAsync(searchServiceUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    //_logger.LogInformation("SearchService successfully acknowledged indexing request for File ID {FileId}", fileId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("SearchService indexing request failed for File ID {FileId}. Status: {StatusCode}, Reason: {Reason}, Body: {ErrorBody}",
                       fileId, response.StatusCode, response.ReasonPhrase, errorBody.Substring(0, Math.Min(errorBody.Length, 500)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send indexing request to SearchService ({Url}) for File ID {FileId}", searchServiceUrl, fileId);
            }
        }

        private async Task NotifySearchServiceDeleteAsync(Guid fileId)
        {
            string searchServiceUrl = $"http://searchservice/api/index/index/{fileId}";
            try
            {
                //_logger.LogInformation("Notifying SearchService to delete index for File ID {FileId} at {Url}", fileId, searchServiceUrl);
                using var client = _httpClientFactory.CreateClient("SearchServiceClient");
                client.Timeout = TimeSpan.FromSeconds(10);

                using var response = await client.DeleteAsync(searchServiceUrl);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SearchService successfully acknowledged index deletion request for File ID {FileId}", fileId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    //_logger.LogWarning("SearchService index deletion request failed for File ID {FileId}. Status: {StatusCode}, Reason: {Reason}, Body: {ErrorBody}",
                      // fileId, response.StatusCode, response.ReasonPhrase, errorBody.Substring(0, Math.Min(errorBody.Length, 500)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send index deletion request to SearchService ({Url}) for File ID {FileId}", searchServiceUrl, fileId);
            }
        }

    }

    public class FileUploadModel
    {
        [Required(ErrorMessage = "Необходимо выбрать файл.")]
        public IFormFile File { get; set; }

        [Required(ErrorMessage = "Необходимо выбрать группу.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "Недопустимое имя группы.")]
        public string TargetGroup { get; set; }
    }
}