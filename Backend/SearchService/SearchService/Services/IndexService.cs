// SearchService/Services/IndexService.cs
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Directory = Lucene.Net.Store.Directory; // Уточняем, т.к. есть System.IO.Directory

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
        string indexPath = configuration["LuceneIndexPath"] ?? "lucene_index"; // Путь из env
        if (!System.IO.Directory.Exists(indexPath))
        {
            System.IO.Directory.CreateDirectory(indexPath);
        }
        _logger.LogInformation("Initializing Lucene Index at: {IndexPath}", indexPath);

        _indexDirectory = FSDirectory.Open(indexPath);
        var analyzer = new StandardAnalyzer(AppLuceneVersion);
        var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND // Создать или добавить
        };
        _writer = new IndexWriter(_indexDirectory, indexConfig);
    }

    public Task IndexFileAsync(Guid fileId, int userId, string textContent)
    {
        if (string.IsNullOrWhiteSpace(textContent))
        {
            _logger.LogWarning("Skipping indexing for File ID {FileId} due to empty text content.", fileId);
            // Возможно, стоит удалить старый индекс, если файл обновился и стал пустым?
            return Task.CompletedTask;
        }

        try
        {
            var doc = new Document
            {
                // Сохраняем ID файла (не анализируем, но индексируем для поиска/удаления)
                new StringField("fileId", fileId.ToString(), Field.Store.YES),
                // Сохраняем ID пользователя (можно использовать для фильтрации)
                new Int32Field("userId", userId, Field.Store.YES), // Используем Int32Field
                // Сохраняем и анализируем текст файла
                new TextField("content", textContent, Field.Store.NO) // NO - т.к. текст может быть большим, не храним его в индексе
            };

            _logger.LogDebug("Indexing document for File ID: {FileId}, User ID: {UserId}", fileId, userId);
            // Используем UpdateDocument для замены, если документ уже есть
            _writer.UpdateDocument(new Term("fileId", fileId.ToString()), doc);
            _writer.Commit(); // Фиксируем изменения (можно делать реже для производительности)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing file ID {FileId}", fileId);
            // TODO: Обработка ошибок - откат? повтор?
        }
        return Task.CompletedTask; // Асинхронность здесь условна
    }

    public Task DeleteFileAsync(Guid fileId)
    {
        try
        {
            _logger.LogInformation("Deleting document from index for File ID: {FileId}", fileId);
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
        _logger.LogInformation("Disposing Lucene IndexWriter.");
        _writer?.Dispose();
        _indexDirectory?.Dispose();
    }
}