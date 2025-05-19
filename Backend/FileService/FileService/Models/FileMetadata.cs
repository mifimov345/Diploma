using System;

namespace FileService.Models
{
    public class FileMetadata
    {
        public Guid Id { get; set; }
        public string OriginalName { get; set; }
        public string StoredFileName { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
        public DateTime UploadedAt { get; set; }
        public int UserId { get; set; }
        public string UserGroup { get; set; }
        public string FilePath { get; set; }
        public string Description { get; set; }
    }
}
