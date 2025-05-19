using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileService.Controllers;
using FileService.Data;
using FileService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileService.Services
{
    public class FileMetadataService : IFileMetadataService
    {
        private readonly FileDbContext _db;
        private readonly ILogger<FileMetadataService> _logger;

        public FileMetadataService(FileDbContext db, ILogger<FileMetadataService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task AddMetadataAsync(FileMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            await _db.FileMetadatas.AddAsync(metadata);
            await _db.SaveChangesAsync();
        }

        public async Task<FileMetadata> GetMetadataByIdAsync(Guid id)
        {
            return await _db.FileMetadatas.FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<IEnumerable<FileMetadata>> GetMetadataByUserAsync(int userId)
        {
            return await _db.FileMetadatas.Where(m => m.UserId == userId).ToListAsync();
        }

        public async Task<IEnumerable<FileMetadata>> GetAllMetadataAsync()
        {
            return await _db.FileMetadatas.ToListAsync();
        }

        public async Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingAdminId)
        {
            if (groupNames == null || !groupNames.Any())
                return await GetMetadataByUserAsync(requestingAdminId);

            return await _db.FileMetadatas
                .Where(m => (m.UserGroup != null && groupNames.Contains(m.UserGroup)) || m.UserId == requestingAdminId)
                .ToListAsync();
        }

        public async Task<IEnumerable<FileMetadata>> GetGroupFilesForUserAsync(int userId, List<string> userGroups)
        {
            if (userGroups == null || !userGroups.Any())
                return Enumerable.Empty<FileMetadata>();

            return await _db.FileMetadatas
                .Where(m => m.UserId != userId && m.UserGroup != null && userGroups.Contains(m.UserGroup))
                .ToListAsync();
        }

        public async Task<string> DeleteMetadataAndFileAsync(Guid id)
        {
            var entity = await _db.FileMetadatas.FirstOrDefaultAsync(m => m.Id == id);
            if (entity != null)
            {
                var path = entity.FilePath;
                _db.FileMetadatas.Remove(entity);
                await _db.SaveChangesAsync();
                return path;
            }
            return null;
        }

        public async Task<IEnumerable<FileMetadata>> SearchByOriginalNameAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups, string scope = "default")
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<FileMetadata>();

            var term = searchTerm.Trim().ToLowerInvariant();
            IQueryable<FileMetadata> query = _db.FileMetadatas;

            if (currentUserRole == AppRoles.User)
            {
                if (scope.Equals("group", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(m => m.UserId != currentUserId && m.UserGroup != null && currentUserGroups.Contains(m.UserGroup));
                }
                else
                {
                    query = query.Where(m => m.UserId == currentUserId);
                }
            }
            else if (currentUserRole == AppRoles.Admin)
            {
                query = query.Where(m => m.UserId == currentUserId || (m.UserGroup != null && currentUserGroups.Contains(m.UserGroup)));
            }

            return await query.Where(m => m.OriginalName.ToLower().Contains(term)).ToListAsync();
        }

        public async Task<IEnumerable<FileMetadata>> SearchMetadataAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups, string scope = "default")
        {
            return await SearchByOriginalNameAsync(searchTerm, currentUserId, currentUserRole, currentUserGroups, scope);
        }

        public async Task<bool> UpdateFileGroupAsync(Guid fileId, string newGroupName, int currentUserId, string currentUserRole, List<string> currentUserGroups)
        {
            _logger.LogInformation("Attempting to update group for File ID {FileId} to '{NewGroup}' by User ID {UserId} (Role: {UserRole})",
                fileId, newGroupName, currentUserId, currentUserRole);

            var metadata = await GetMetadataByIdAsync(fileId);

            if (metadata == null)
            {
                _logger.LogWarning("UpdateFileGroup: File metadata not found for ID {FileId}", fileId);
                return false;
            }

            if (string.IsNullOrWhiteSpace(newGroupName) || newGroupName.Equals("System", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid group name specified.");

            bool canUpdate = false;

            if (currentUserRole == AppRoles.SuperAdmin)
            {
                canUpdate = true;
            }
            else if (currentUserRole == AppRoles.Admin)
            {
                bool isAdminOwner = metadata.UserId == currentUserId;
                bool isFileInAdminGroup = metadata.UserGroup != null && currentUserGroups.Contains(metadata.UserGroup);
                canUpdate = isAdminOwner || isFileInAdminGroup;
            }
            else if (currentUserRole == AppRoles.User)
            {
                canUpdate = metadata.UserId == currentUserId;
            }

            if (!canUpdate) return false;

            if (currentUserRole != AppRoles.SuperAdmin && !currentUserGroups.Contains(newGroupName))
                return false;

            metadata.UserGroup = newGroupName;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
