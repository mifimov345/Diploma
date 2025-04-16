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
    }

    public class FileMetadataService : IFileMetadataService
    {
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
            var userFiles = _metadataStore.Values.Where(m => m.UserId == userId).ToList();
            return Task.FromResult<IEnumerable<FileMetadata>>(userFiles);
        }

        public Task<IEnumerable<FileMetadata>> GetAllMetadataAsync()
        {
            return Task.FromResult<IEnumerable<FileMetadata>>(_metadataStore.Values.ToList());
        }

        // Получаем файлы пользователей, чья UserGroup (при загрузке) входит в список groupNames и файлы админа
        public Task<IEnumerable<FileMetadata>> GetMetadataByUserGroupsAsync(List<string> groupNames, int requestingAdminId)
        {
            if (groupNames == null || !groupNames.Any())
                return Task.FromResult(Enumerable.Empty<FileMetadata>());

            var groupFiles = _metadataStore.Values
                .Where(m => (m.UserGroup != null && groupNames.Contains(m.UserGroup)) || m.UserId == requestingAdminId) // Файлы из групп ИЛИ собственные файлы админа
                .Distinct() // Убираем дубликаты, если админ сам в этой группе
                .ToList();
            return Task.FromResult<IEnumerable<FileMetadata>>(groupFiles);
        }

        public Task<string> DeleteMetadataAndFileAsync(Guid id)
        {
            if (_metadataStore.TryRemove(id, out var metadata))
            {
                return Task.FromResult(metadata.FilePath); // Возвращаем путь для физического удаления
            }
            return Task.FromResult<string>(null); // Файл не найден в метаданных
        }
    }
}