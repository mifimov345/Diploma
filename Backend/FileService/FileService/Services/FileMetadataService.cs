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
    }

    public class FileMetadataService : IFileMetadataService
    {
        // Потокобезопасный словарь для хранения в памяти
        private readonly ConcurrentDictionary<Guid, FileMetadata> _metadataStore = new();

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
            // Используем .ToList() здесь, чтобы создать копию и избежать проблем с многопоточностью при итерации
            var userFiles = _metadataStore.Values.Where(m => m.UserId == userId).ToList();
            return Task.FromResult<IEnumerable<FileMetadata>>(userFiles);
        }

        public Task<IEnumerable<FileMetadata>> GetAllMetadataAsync()
        {
            return Task.FromResult<IEnumerable<FileMetadata>>(_metadataStore.Values.ToList());
        }

        public Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingAdminId)
        {
            if (groupNames == null || !groupNames.Any())
            {
                return GetMetadataByUserAsync(requestingAdminId);
            }

            var groupFiles = _metadataStore.Values
                .Where(m => (m.UserGroup != null && groupNames.Contains(m.UserGroup)) || m.UserId == requestingAdminId) // Файлы из групп ИЛИ собственные файлы админа
                .Distinct() // Убираем дубликаты, если админ сам в этой группе
                .ToList(); // Создаем копию

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

        // Поиск файлов по имени
        public Task<IEnumerable<FileMetadata>> SearchMetadataAsync(string searchTerm, int currentUserId, string currentUserRole, List<string> currentUserGroups)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return Task.FromResult(Enumerable.Empty<FileMetadata>()); // Пустой запрос - пустой результат
            }

            // Приводим к нижнему регистру для поиска без учета регистра
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