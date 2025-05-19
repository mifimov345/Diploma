using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SearchService.Services;
using Directory = Lucene.Net.Store.Directory;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;

public class IndexService : IIndexService, IDisposable
{
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    private readonly Directory _indexDirectory;
    private readonly IndexWriter _writer;
    private readonly ILogger<IndexService> _logger;
    private readonly StandardAnalyzer _analyzer;

    public IndexService(IConfiguration configuration, ILogger<IndexService> logger)
    {
        _logger = logger;
        string indexPath = configuration["LuceneIndexPath"] ?? "lucene_index";
        if (!System.IO.Directory.Exists(indexPath))
        {
            System.IO.Directory.CreateDirectory(indexPath);
        }

        _indexDirectory = FSDirectory.Open(indexPath);
        _analyzer = new StandardAnalyzer(AppLuceneVersion);
        var indexConfig = new IndexWriterConfig(AppLuceneVersion, _analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND
        };
        _writer = new IndexWriter(_indexDirectory, indexConfig);
    }

    public Task IndexFileAsync(Guid fileId, int userId, string textContent)
    {
        if (string.IsNullOrWhiteSpace(textContent))
            return Task.CompletedTask;

        try
        {
            var doc = new Document
            {
                new StringField("fileId", fileId.ToString(), Field.Store.YES),
                new Int32Field("userId", userId, Field.Store.YES),
                new TextField("content", textContent, Field.Store.YES)
            };
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
            _writer.DeleteDocuments(new Term("fileId", fileId.ToString()));
            _writer.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file ID {FileId} from index", fileId);
        }
        return Task.CompletedTask;
    }

    public List<Guid> Search(string queryText)
    {
        var results = new List<Guid>();
        try
        {
            using var reader = DirectoryReader.Open(_writer, applyAllDeletes: true);
            var searcher = new IndexSearcher(reader);

            var parser = new MultiFieldQueryParser(AppLuceneVersion, new[] { "content", "name" }, _analyzer);
            var query = parser.Parse(QueryParserBase.Escape(queryText));

            var hits = searcher.Search(query, 50).ScoreDocs; // 50 результатов макс

            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                if (Guid.TryParse(doc.Get("fileId"), out var id))
                    results.Add(id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Lucene index for query '{QueryText}'", queryText);
        }
        return results;
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _indexDirectory?.Dispose();
    }
}