using FileService.Controllers;
using FileService.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileService.Services
{
    public interface IFileMetadataService
    {
        Task AddMetadataAsync(FileMetadata metadata);
        Task<FileMetadata> GetMetadataByIdAsync(Guid id);
        Task<IEnumerable<FileMetadata>> GetMetadataByUserAsync(int userId);
        Task<IEnumerable<FileMetadata>> GetAllMetadataAsync();
        Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingAdminId);
        Task<string> DeleteMetadataAndFileAsync(Guid id);
        Task<IEnumerable<FileMetadata>> SearchMetadataAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups); // Поиск
        Task<bool> UpdateFileGroupAsync(Guid fileId, string newGroupName, int currentUserId, string currentUserRole, List<string> currentUserGroups);

    }

    public class FileMetadataService : IFileMetadataService
    {
        private readonly ConcurrentDictionary<Guid, FileMetadata> _metadataStore = new();

        private readonly ILogger<FileMetadataService> _logger;

        public FileMetadataService(ILogger<FileMetadataService> logger)
        {
            _logger = logger;
        }

        public Task AddMetadataAsync(FileMetadata metadata)
        {
            _metadataStore.TryAdd(metadata.Id, metadata);
            return Task.CompletedTask;
        }

        public Task<FileMetadata> GetMetadataByIdAsync(Guid id)
        {
            _metadataStore.TryGetValue(id, out var metadata);
            return Task.FromResult(metadata);
        }

        public Task<IEnumerable<FileMetadata>> GetMetadataByUserAsync(int userId)
        {
            var userFiles = _metadataStore.Values.Where(m => m.UserId == userId).ToList();
            return Task.FromResult<IEnumerable<FileMetadata>>(userFiles);
        }

        public Task<IEnumerable<FileMetadata>> GetAllMetadataAsync()
        {
            return Task.FromResult<IEnumerable<FileMetadata>>(_metadataStore.Values.ToList());
        }

        public async Task<bool> UpdateFileGroupAsync(Guid fileId, string newGroupName, int currentUserId, string currentUserRole, List<string> currentUserGroups)
        {
            _logger.LogInformation("Attempting to update group for File ID {FileId} to '{NewGroup}' by User ID {UserId} (Role: {UserRole})",
                 fileId, newGroupName, currentUserId, currentUserRole);

            FileMetadata? metadata = await GetMetadataByIdAsync(fileId);

            if (metadata == null)
            {
                _logger.LogWarning("UpdateFileGroup: File metadata not found for ID {FileId}", fileId);
                return false;
            }

            bool canUpdate = false;
            bool checkUserGroups = false;

            if (currentUserRole == AppRoles.SuperAdmin)
            {
                canUpdate = true;
            }
            else if (metadata.UserId == currentUserId)
            {
                canUpdate = true;
                checkUserGroups = true;
            }

            if (!canUpdate)
            {
                //_logger.LogWarning("UpdateFileGroup: Access DENIED for User ID {UserId} to change group for File ID {FileId}", currentUserId, fileId);
                return false;
            }

            if (checkUserGroups && !currentUserGroups.Contains(newGroupName))
            {
                //_logger.LogWarning("UpdateFileGroup: User ID {UserId} attempted to assign group '{NewGroup}' which they do not belong to. File ID: {FileId}", currentUserId, newGroupName, fileId);
                return false;
            }
            if (string.IsNullOrWhiteSpace(newGroupName))
            {
                //_logger.LogWarning("UpdateFileGroup: Attempted to assign empty or whitespace group name for File ID {FileId}", fileId);
                return false;
            }
            string oldGroup = metadata.UserGroup ?? "N/A";
            metadata.UserGroup = newGroupName;

            //_logger.LogInformation("Successfully updated group for File ID {FileId} from '{OldGroup}' to '{NewGroup}' by User ID {UserId}",
                //fileId, oldGroup, newGroupName, currentUserId);

            return true;
        }

        public Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingAdminId)
        {
            if (groupNames == null || !groupNames.Any())
            {
                return GetMetadataByUserAsync(requestingAdminId);
            }

            var groupFiles = _metadataStore.Values
                .Where(m => (m.UserGroup != null && groupNames.Contains(m.UserGroup)) || m.UserId == requestingAdminId)
                .Distinct()
                .ToList();

            return Task.FromResult<IEnumerable<FileMetadata>>(groupFiles);
        }

        public Task<string> DeleteMetadataAndFileAsync(Guid id)
        {
            if (_metadataStore.TryRemove(id, out var metadata))
            {
                return Task.FromResult(metadata.FilePath);
            }
            return Task.FromResult<string>(null);
        }

        public Task<IEnumerable<FileMetadata>> SearchMetadataAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return Task.FromResult(Enumerable.Empty<FileMetadata>());
            }

            var term = searchTerm.Trim().ToLowerInvariant();

            IEnumerable<FileMetadata> query = _metadataStore.Values;

            if (currentUserRole == AppRoles.Admin)
            {
                query = query.Where(m => m.UserId == currentUserId || (m.UserGroup != null && currentUserGroups.Contains(m.UserGroup)));
            }
            else if (currentUserRole == AppRoles.User)
            {
                query = query.Where(m => m.UserId == currentUserId);
            }

            var results = query.Where(m => m.OriginalName.ToLowerInvariant().Contains(term))
                               .ToList(); // Материализуем результат в список (копию)

            return Task.FromResult<IEnumerable<FileMetadata>>(results);
        }
    }
}