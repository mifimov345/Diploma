using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileService.Models;

namespace FileService.Services
{
    public interface IFileMetadataService
    {
        Task AddMetadataAsync(FileMetadata metadata);
        Task<FileMetadata> GetMetadataByIdAsync(Guid id);
        Task<IEnumerable<FileMetadata>> GetMetadataByUserAsync(int userId);
        Task<IEnumerable<FileMetadata>> GetAllMetadataAsync();
        Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingUserId);
        Task<string> DeleteMetadataAndFileAsync(Guid id);
        Task<IEnumerable<FileMetadata>> GetGroupFilesForUserAsync(int userId, List<string> userGroups);
        Task<bool> UpdateFileGroupAsync(Guid fileId, string newGroupName, int currentUserId, string currentUserRole, List<string> currentUserGroups);

        Task<IEnumerable<FileMetadata>> SearchMetadataAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups, string scope = "default");
        Task<IEnumerable<FileMetadata>> SearchByOriginalNameAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups, string scope = "default");
    }
}
