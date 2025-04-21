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
    public interface ISearcherService
    {
        Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null);
    }

    public class SearcherService : ISearcherService
    {
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_CURRENT;
        private readonly Directory _indexDirectory;
        private readonly ILogger<SearcherService> _logger;
        private readonly string _indexPath;

        public SearcherService(IConfiguration configuration, ILogger<SearcherService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _indexPath = configuration["LuceneIndexPath"] ?? "lucene_index";
            //_logger.LogInformation("SearcherService attempting to use Lucene Index at: {IndexPath}", _indexPath);

            try
            {
                if (!System.IO.Directory.Exists(_indexPath))
                {
                    _logger.LogWarning("Lucene index directory does not exist at startup: {IndexPath}. It might be created later.", _indexPath);
                }
                else
                {
                    _indexDirectory = FSDirectory.Open(_indexPath);
                    //_logger.LogInformation("Successfully opened Lucene index directory for reading: {IndexPath}", _indexPath);
                    if (IndexWriter.IsLocked(_indexDirectory))
                    {
                        _logger.LogWarning("Lucene index directory is locked: {IndexPath}. Unlocking...", _indexPath);
                        IndexWriter.Unlock(_indexDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Failed to open Lucene index directory at startup: {IndexPath}. Search will likely fail.", _indexPath);
                _indexDirectory = null;
            }
        }

        public Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null)
        {
            //_logger.LogInformation("--- SearcherService: Starting search. Term='{SearchTerm}', UserFilter={UserIdFilter}", searchTerm, userIdFilter?.ToString() ?? "None");

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                //_logger.LogWarning("Search term is empty or whitespace. Returning empty results.");
                return Task.FromResult(Enumerable.Empty<Guid>());
            }

            Directory currentDirectory = null;
            DirectoryReader reader = null;
            try
            {
                if (_indexDirectory == null)
                {
                    if (!System.IO.Directory.Exists(_indexPath))
                    {
                        //_logger.LogWarning("Search aborted: Index directory does not exist at {IndexPath}", _indexPath);
                        return Task.FromResult(Enumerable.Empty<Guid>());
                    }
                    currentDirectory = FSDirectory.Open(_indexPath);
                    //_logger.LogInformation("Opened index directory on demand: {IndexPath}", _indexPath);
                }
                else
                {
                    currentDirectory = _indexDirectory;
                }

                if (!DirectoryReader.IndexExists(currentDirectory))
                {
                    //_logger.LogWarning("Search aborted: No Lucene index found in directory {IndexPath}", _indexPath);
                    if (currentDirectory != _indexDirectory) currentDirectory.Dispose();
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                reader = DirectoryReader.Open(currentDirectory);
                var searcher = new IndexSearcher(reader);
                var analyzer = new StandardAnalyzer(AppLuceneVersion);
                var mainQueryParser = new QueryParser(AppLuceneVersion, "content", analyzer);
                mainQueryParser.AllowLeadingWildcard = false;

                Query finalQuery = null;

                //_logger.LogDebug("--- SearcherService: Parsing search term: '{SearchTerm}'", searchTerm);
                try
                {
                    string escapedTerm = QueryParserBase.Escape(searchTerm);
                    if (string.IsNullOrWhiteSpace(escapedTerm))
                    {
                        //_logger.LogWarning("Search term became empty after escaping: '{OriginalTerm}'. Aborting search.", searchTerm);
                        return Task.FromResult(Enumerable.Empty<Guid>());
                    }

                    Query contentQuery = mainQueryParser.Parse(escapedTerm);
                    //_logger.LogDebug("--- SearcherService: Parsed contentQuery: {QueryText}", contentQuery.ToString());
                    finalQuery = contentQuery;

                    // Добавляем фильтр по userId, если он задан
                    if (userIdFilter.HasValue)
                    {
                        //_logger.LogDebug("--- SearcherService: Adding userId filter: {UserId}", userIdFilter.Value);
                        Query userQuery = NumericRangeQuery.NewInt32Range("userId", userIdFilter.Value, userIdFilter.Value, true, true);
                        var booleanQuery = new BooleanQuery {
                             { contentQuery, Occur.MUST },
                             { userQuery, Occur.MUST }
                         };
                        finalQuery = booleanQuery;
                    }
                }
                catch (ParseException parseEx)
                {
                    //_logger.LogError(parseEx, "--- SearcherService: Error PARSING Lucene query for term: '{SearchTerm}'", searchTerm);
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }
                catch (Exception ex)
                {
                    //_logger.LogError(ex, "--- SearcherService: Unexpected error during query parsing/building for term: '{SearchTerm}'", searchTerm);
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                if (finalQuery == null)
                {
                    //_logger.LogError("--- SearcherService: Final query is null after parsing term '{SearchTerm}'. Cannot search.", searchTerm);
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                //_logger.LogInformation("--- SearcherService: Executing Lucene search. Final Query: {Query}, UserFilter: {UserFilter}", finalQuery.ToString(), userIdFilter?.ToString() ?? "None");
                TopDocs topDocs = null;
                int totalHits = 0;
                var results = new List<Guid>();

                try
                {
                    int maxResults = 100;
                    topDocs = searcher.Search(finalQuery, maxResults);
                    totalHits = topDocs.TotalHits;
                    //_logger.LogInformation("--- SearcherService: Search found {HitCount} total hits. Returning top {MaxResults}.", totalHits, maxResults);

                    foreach (var scoreDoc in topDocs.ScoreDocs)
                    {
                        Document doc = searcher.Doc(scoreDoc.Doc);
                        string fileIdString = doc.Get("fileId");
                        if (Guid.TryParse(fileIdString, out Guid fileId))
                        {
                            results.Add(fileId);
                        }
                        else
                        {
                            _logger.LogWarning("Found document in index (DocID: {LuceneDocId}) with invalid fileId format: {InvalidId}", scoreDoc.Doc, fileIdString);
                        }
                    }
                }
                catch (Exception searchEx)
                {
                    //_logger.LogError(searchEx, "--- SearcherService: Error EXECUTING Lucene search. Query: {Query}", finalQuery.ToString());
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                return Task.FromResult<IEnumerable<Guid>>(results);
            }
            catch (IndexNotFoundException indexEx)
            {
                //_logger.LogWarning(indexEx, "Lucene index not found at {IndexPath}", _indexPath);
                return Task.FromResult(Enumerable.Empty<Guid>());
            }
            catch (IOException ioEx)
            {
                //_logger.LogError(ioEx, "IO error opening or reading Lucene index at {IndexPath}", _indexPath);
                return Task.FromResult(Enumerable.Empty<Guid>()); // Возвращаем пусто, т.к. поиск невозможен
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Unexpected error during search for term: {SearchTerm}", searchTerm);
                return Task.FromResult(Enumerable.Empty<Guid>());
            }
            finally
            {
                reader?.Dispose();
                //_logger.LogDebug("--- SearcherService: IndexReader disposed.");
                if (currentDirectory != null && currentDirectory != _indexDirectory)
                {
                    currentDirectory.Dispose();
                    //_logger.LogDebug("--- SearcherService: On-demand index directory disposed.");
                }
            }
        }

    }
}