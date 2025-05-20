using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Directory = Lucene.Net.Store.Directory;

namespace SearchService.Services
{
    public class SearcherService : ISearcherService
    {
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_CURRENT;
        private readonly Directory _indexDirectory;
        private readonly ILogger<SearcherService> _logger;
        private readonly string _indexPath;
        private readonly MinioService _minioService;

        public SearcherService(IConfiguration configuration, ILogger<SearcherService> logger, MinioService minioService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _indexPath = configuration["LuceneIndexPath"] ?? "lucene_index";
            _minioService = minioService;
            try
            {
                if (!System.IO.Directory.Exists(_indexPath))
                {
                    // Directory may be created later
                }
                else
                {
                    _indexDirectory = FSDirectory.Open(_indexPath);
                    if (IndexWriter.IsLocked(_indexDirectory))
                    {
                        IndexWriter.Unlock(_indexDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Lucene index directory at startup: {IndexPath}.", _indexPath);
                _indexDirectory = null;
            }
        }

        public Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null)
        {
            _logger.LogDebug("I am alive");
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Task.FromResult(Enumerable.Empty<Guid>());

            Directory currentDirectory = null;
            DirectoryReader reader = null;
            try
            {
                if (_indexDirectory == null)
                {
                    if (!System.IO.Directory.Exists(_indexPath))
                        return Task.FromResult(Enumerable.Empty<Guid>());
                    currentDirectory = FSDirectory.Open(_indexPath);
                }
                else
                {
                    currentDirectory = _indexDirectory;
                }
                _logger.LogDebug($"currentdirectory - {currentDirectory}");
                if (!DirectoryReader.IndexExists(currentDirectory))
                    return Task.FromResult(Enumerable.Empty<Guid>());
                _logger.LogDebug("crushing through");
                reader = DirectoryReader.Open(currentDirectory);
                var searcher = new IndexSearcher(reader);
                var analyzer = new StandardAnalyzer(AppLuceneVersion);

                var parser = new QueryParser(AppLuceneVersion, "content", analyzer);
                Query query;
                try
                {
                    query = parser.Parse(searchTerm);
                }
                catch (ParseException px)
                {
                    _logger.LogError(px, "  --- exception");
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                Query finalQuery = query;
                if (userIdFilter.HasValue)
                {
                    Query userQuery = NumericRangeQuery.NewInt32Range("userId", userIdFilter.Value, userIdFilter.Value, true, true);
                    var booleanQuery = new BooleanQuery
                    {
                        { query, Occur.MUST },
                        { userQuery, Occur.MUST }
                    };
                    finalQuery = booleanQuery;
                }

                if (finalQuery == null)
                    return Task.FromResult(Enumerable.Empty<Guid>());

                int maxResults = 100;
                TopDocs topDocs = searcher.Search(finalQuery, maxResults);
                _logger.LogDebug($"topDocks - {topDocs}");
                var results = new List<Guid>();
                foreach (var scoreDoc in topDocs.ScoreDocs)
                {
                    _logger.LogDebug($"we've found one");
                    Document doc = searcher.Doc(scoreDoc.Doc);
                    string fileIdString = doc.Get("fileId");
                    if (Guid.TryParse(fileIdString, out Guid fileId))
                    {
                        results.Add(fileId);
                    }
                }
                _logger.LogDebug($"results - {results}");
                return Task.FromResult<IEnumerable<Guid>>(results);
            }
            catch
            {
                return Task.FromResult(Enumerable.Empty<Guid>());
            }
            finally
            {
                reader?.Dispose();
                if (currentDirectory != null && currentDirectory != _indexDirectory)
                    currentDirectory.Dispose();
            }
        }
    }
}