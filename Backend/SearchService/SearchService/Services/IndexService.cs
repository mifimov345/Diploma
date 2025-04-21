using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Directory = Lucene.Net.Store.Directory;

public interface IIndexService
{
    Task IndexFileAsync(Guid fileId, int userId, string textContent);
    Task DeleteFileAsync(Guid fileId);
}

public class IndexService : IIndexService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    private readonly Directory _indexDirectory;
    private readonly IndexWriter _writer;
    private readonly ILogger<IndexService> _logger;

    public IndexService(IConfiguration configuration, ILogger<IndexService> logger)
    {
        _logger = logger;
        string indexPath = configuration["LuceneIndexPath"] ?? "lucene_index";
        if (!System.IO.Directory.Exists(indexPath))
        {
            System.IO.Directory.CreateDirectory(indexPath);
        }
        //_logger.LogInformation("Initializing Lucene Index at: {IndexPath}", indexPath);

        _indexDirectory = FSDirectory.Open(indexPath);
        var analyzer = new StandardAnalyzer(AppLuceneVersion);
        var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND
        };
        _writer = new IndexWriter(_indexDirectory, indexConfig);
    }

    public Task IndexFileAsync(Guid fileId, int userId, string textContent)
    {
        if (string.IsNullOrWhiteSpace(textContent))
        {
            //_logger.LogWarning("Skipping indexing for File ID {FileId} due to empty text content.", fileId);
            return Task.CompletedTask;
        }

        try
        {
            var doc = new Document
            {
                new StringField("fileId", fileId.ToString(), Field.Store.YES),
                new Int32Field("userId", userId, Field.Store.YES),
                new TextField("content", textContent, Field.Store.NO)
            };

            //_logger.LogDebug("Indexing document for File ID: {FileId}, User ID: {UserId}", fileId, userId);
            _writer.UpdateDocument(new Term("fileId", fileId.ToString()), doc);
            _writer.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing file ID {FileId}", fileId);
        }
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(Guid fileId)
    {
        try
        {
            //_logger.LogInformation("Deleting document from index for File ID: {FileId}", fileId);
            _writer.DeleteDocuments(new Term("fileId", fileId.ToString()));
            _writer.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file ID {FileId} from index", fileId);
        }
        return Task.CompletedTask;
    }


    public void Dispose()
    {
        //_logger.LogInformation("Disposing Lucene IndexWriter.");
        _writer?.Dispose();
        _indexDirectory?.Dispose();
    }
}