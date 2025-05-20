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
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Net.Http;

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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MinioService _minioService;

        public FileController(
            IFileMetadataService metadataService,
            ILogger<FileController> logger,
            IHttpClientFactory httpClientFactory,
            MinioService minioService)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _minioService = minioService ?? throw new ArgumentNullException(nameof(minioService));
        }

        internal const string GroupClaimTypeUri = "http://schemas.xmlsoap.org/claims/Group";

        // ======== UPLOAD ========
        [HttpPost("upload")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.SelectMany(x => x.Value.Errors.Select(e => e.ErrorMessage));
                    _logger.LogError("ModelState is invalid: " + string.Join("; ", errors));
                    return BadRequest(ModelState);
                }

                long maxFileSize = 100 * 1024 * 1024;
                if (model.File == null)
                    return BadRequest("Файл не был передан.");

                if (model.File.Length > maxFileSize)
                    return BadRequest($"File size exceeds the {maxFileSize / 1024 / 1024} MB limit.");

                int userId;
                List<string> userGroups;
                string userRoleClaimValue;
                try
                {
                    userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                    userRoleClaimValue = User.FindFirstValue(ClaimTypes.Role);
                    userGroups = User.FindAll(GroupClaimTypeUri)
                                             .Select(c => c.Value)
                                             .ToList();
                    if (userGroups == null || !userGroups.Any())
                        return StatusCode(StatusCodes.Status403Forbidden, "You must belong to at least one group to upload files.");

                    if (!userGroups.Contains(model.TargetGroup))
                        return StatusCode(StatusCodes.Status403Forbidden, $"You do not have permission to upload to group '{model.TargetGroup}'.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка парсинга user claims");
                    return Unauthorized("Invalid user claims in token.");
                }

                var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(model.File.FileName)}";
                var fileKey = $"{userId}/{storedFileName}";

                FileMetadata metadata = null;
                try
                {
                    using (var stream = model.File.OpenReadStream())
                    {
                        await _minioService.UploadFileAsync(fileKey, stream, model.File.ContentType);
                    }

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
                        FilePath = fileKey, // путь в бакете
                        Description = ""
                    };
                    await _metadataService.AddMetadataAsync(metadata);

                    string nameWithoutExtension = Path.GetFileNameWithoutExtension(metadata.OriginalName);
                    string extractedText = "";
                    try
                    {
                        var client = _httpClientFactory.CreateClient();

                        // Качаем файл из MinIO для передачи в text-extract
                        using var fileStream = await _minioService.DownloadFileAsync(fileKey);
                        using var form = new MultipartFormDataContent();
                        form.Add(new StreamContent(fileStream), "file", metadata.OriginalName);
                        form.Add(new StringContent(metadata.ContentType ?? ""), "contentType");

                        var response = await client.PostAsync("http://searchservice/api/text-extract/extract", form);

                        if (response.IsSuccessStatusCode)
                        {
                            extractedText = await response.Content.ReadAsStringAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Не удалось извлечь текст, будет проиндексировано только имя без расширения.");
                    }

                    string textToIndex = nameWithoutExtension;
                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        textToIndex += "\n" + extractedText;
                    }

                    try
                    {
                        var client = _httpClientFactory.CreateClient();

                        var indexRequest = new
                        {
                            FileId = metadata.Id,
                            UserId = metadata.UserId,
                            OriginalName = metadata.OriginalName,
                            ContentType = metadata.ContentType,
                            TextContent = textToIndex
                        };

                        string jsonRequest = JsonSerializer.Serialize(indexRequest);
                        _logger.LogInformation("[Lucene] Отправляется запрос индексации: {Json}", jsonRequest);

                        var indexResponse = await client.PostAsJsonAsync(
                            "http://searchservice/api/index/index",
                            indexRequest
                        );

                        string responseContent = await indexResponse.Content.ReadAsStringAsync();

                        if (!indexResponse.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("[Lucene] Indexing request failed for FileId {FileId}: {Status} {ReasonPhrase}, Response: {Content}",
                                metadata.Id, indexResponse.StatusCode, indexResponse.ReasonPhrase, responseContent);
                        }
                        else
                        {
                            _logger.LogInformation("[Lucene] Индексация прошла успешно: FileId {FileId}, Status: {Status}, Response: {Content}",
                                metadata.Id, indexResponse.StatusCode, responseContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Lucene] Failed to index file {FileId} in SearchService", metadata.Id);
                    }

                    return Ok(new
                    {
                        FileId = metadata.Id,
                        FileName = metadata.OriginalName,
                        Group = metadata.UserGroup
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка во время сохранения файла");
                    try { await _minioService.DeleteFileAsync(fileKey); } catch { }
                    return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error during file upload.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Глобальная ошибка UploadFile");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error during file upload (outer catch).");
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

        // ======== FILES ========
        [HttpGet("files")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> GetFiles([FromQuery] string scope = "default")
        {
            int userId;
            string userRole;
            List<string> userGroups;

            try
            {
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                userRole = User.FindFirstValue(ClaimTypes.Role);
                userGroups = User.FindAll(GroupClaimTypeUri).Select(c => c.Value).ToList();
            }
            catch (Exception)
            {
                return Unauthorized("Invalid user claims in token.");
            }

            IEnumerable<FileMetadata> files = Enumerable.Empty<FileMetadata>();

            try
            {
                if (userRole == AppRoles.SuperAdmin)
                {
                    files = await _metadataService.GetAllMetadataAsync();
                }
                else if (userRole == AppRoles.Admin)
                {
                    if (scope.Equals("all", StringComparison.OrdinalIgnoreCase))
                        files = await _metadataService.GetMetadataByUserGroupsAsync(userGroups, userId);
                    else
                        files = await _metadataService.GetMetadataByUserAsync(userId);
                }
                else
                {
                    files = await _metadataService.GetMetadataByUserAsync(userId);
                }

                var result = files.Select(f => new Dictionary<string, object>
                {
                    { "Id", f.Id },
                    { "OriginalName", f.OriginalName },
                    { "UserGroup", f.UserGroup },
                    { "UserId", f.UserId },
                    { "SizeBytes", f.SizeBytes },
                    { "UploadedAt", f.UploadedAt },
                    { "ContentType", f.ContentType }
                });

                return Ok(result);
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving files.");
            }
        }

        // ======== SEARCH (Отключить) ========
        [HttpGet("search")]
        public IActionResult SearchFiles([FromQuery] string term, [FromQuery] string scope = "default")
        {
            return StatusCode(StatusCodes.Status501NotImplemented, "Search is only available via SearchService.");
        }

        // ======== DOWNLOAD ========
        [HttpGet("download/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DownloadFile(Guid id, [FromQuery] bool inline = false)
        {
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                currentUserGroups = User.FindAll(GroupClaimTypeUri).Select(c => c.Value).ToList();
            }
            catch (Exception)
            {
                return Unauthorized("Invalid user claims in token.");
            }

            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(id); }
            catch { return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information."); }

            if (metadata == null)
                return NotFound("File metadata not found.");

            bool canAccess = false;
            if (currentUserRole == AppRoles.SuperAdmin) canAccess = true;
            else if (currentUserRole == AppRoles.Admin)
            {
                canAccess = (metadata.UserId == currentUserId) || (metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup));
            }
            else if (currentUserRole == AppRoles.User) canAccess = (metadata.UserId == currentUserId);

            if (!canAccess)
                return Forbid("You do not have permission to access this file.");

            if (string.IsNullOrEmpty(metadata.FilePath))
                return NotFound("File path information is missing.");

            try
            {
                var fileStream = await _minioService.DownloadFileAsync(metadata.FilePath);
                if (fileStream.Length == 0) return NotFound("File appears to be empty on the server.");

                var contentDisposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
                contentDisposition.SetHttpFileName(metadata.OriginalName);
                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

                return File(fileStream, metadata.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
            }
            catch
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        // ======== DELETE ========
        [HttpDelete("files/{id}")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role);
                currentUserGroups = User.FindAll(GroupClaimTypeUri).Select(c => c.Value).ToList();
            }
            catch (Exception)
            {
                return Unauthorized("Invalid user claims in token.");
            }

            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(id); }
            catch { return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving file information for deletion."); }

            if (metadata == null)
                return NotFound("File metadata not found.");

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
                return Forbid("You do not have permission to delete this file.");

            var fileKey = metadata.FilePath; // в FilePath теперь путь в бакете
            var filePath = await _metadataService.DeleteMetadataAndFileAsync(id);

            // Удаляем файл из MinIO
            if (!string.IsNullOrEmpty(fileKey))
            {
                try { await _minioService.DeleteFileAsync(fileKey); } catch { }
            }
            if (filePath != null)
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var response = await client.DeleteAsync($"http://searchservice/api/index/delete/{id}");
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Failed to remove file {FileId} from SearchService index: {Status} {Reason}", id, response.StatusCode, response.ReasonPhrase);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling SearchService to delete index for file {FileId}", id);
                }
            }
            else
                return NotFound("File metadata not found (or already deleted).");

            return NoContent();
        }

        // ======== UPDATE GROUP ========
        [HttpPut("files/{id}/group")]
        [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin},{AppRoles.User}")]
        public async Task<IActionResult> UpdateFileGroup(Guid id, [FromBody] UpdateFileGroupRequest model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            int currentUserId;
            string currentUserRole;
            List<string> currentUserGroups;
            try
            {
                currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "";
                currentUserGroups = User.FindAll(GroupClaimTypeUri).Select(c => c.Value).ToList();
                if (string.IsNullOrEmpty(currentUserRole)) throw new Exception("User role claim is missing.");
            }
            catch (Exception)
            {
                return Unauthorized("Invalid user claims in token.");
            }
            try
            {
                bool success = await _metadataService.UpdateFileGroupAsync(id, model.NewGroup, currentUserId, currentUserRole, currentUserGroups);

                if (success) return NoContent();
                else
                {
                    var metadataExists = await _metadataService.GetMetadataByIdAsync(id) != null;
                    if (!metadataExists) return NotFound("File metadata not found.");
                    else return Forbid("Insufficient permissions or invalid target group specified.");
                }
            }
            catch (Exception) { return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while updating groups."); }
        }
        public class UpdateFileGroupRequest
        {
            [Required(ErrorMessage = "Необходимо указать новую группу.")]
            [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "Имя группы может содержать только буквы, цифры, _, -, .")]
            public string NewGroup { get; set; } = string.Empty;
        }

        // ======== INTERNAL DOWNLOAD ========
        [HttpGet("internal/download/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> InternalDownloadFile(Guid id)
        {
            FileMetadata metadata = null;
            try { metadata = await _metadataService.GetMetadataByIdAsync(id); }
            catch { return StatusCode(500); }

            if (metadata == null || string.IsNullOrEmpty(metadata.FilePath)) return NotFound();

            try
            {
                var fileStream = await _minioService.DownloadFileAsync(metadata.FilePath);
                return File(fileStream, metadata.ContentType ?? "application/octet-stream");
            }
            catch
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to serve internal file download.");
            }
        }
    }
}
