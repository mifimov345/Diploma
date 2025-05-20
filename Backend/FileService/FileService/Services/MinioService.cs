using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace FileService.Services
{
    public class MinioService
    {
        private readonly AmazonS3Client _client;
        private readonly string _bucket;

        public MinioService(IConfiguration configuration)
        {
            var config = configuration.GetSection("Minio");
            var endpoint = config.GetValue<string>("Endpoint");
            var accessKey = config.GetValue<string>("AccessKey");
            var secretKey = config.GetValue<string>("SecretKey");
            _bucket = config.GetValue<string>("Bucket");

            var s3Config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
            };

            _client = new AmazonS3Client(accessKey, secretKey, s3Config);
        }

        public async Task UploadFileAsync(string fileName, Stream fileStream, string contentType)
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = fileName,
                InputStream = fileStream,
                ContentType = contentType
            };
            await _client.PutObjectAsync(putRequest);
        }

        public async Task<Stream> DownloadFileAsync(string fileName)
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _bucket,
                Key = fileName
            };
            var response = await _client.GetObjectAsync(getRequest);
            MemoryStream ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        public async Task DeleteFileAsync(string fileName)
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = fileName
            };
            await _client.DeleteObjectAsync(deleteRequest);
        }
    }
}
