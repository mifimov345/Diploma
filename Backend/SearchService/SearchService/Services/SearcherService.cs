using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Configuration; // Для IConfiguration
using Microsoft.Extensions.Logging;     // Для ILogger
using System;
using System.Collections.Generic;
using System.IO;                        // Для Path, DirectoryNotFoundException, IOException
using System.Linq;                      // Для Enumerable
using System.Threading.Tasks;           // Для Task
using Directory = Lucene.Net.Store.Directory; // Уточняем, т.к. есть System.IO.Directory

namespace SearchService.Services
{
    public interface ISearcherService
    {
        // Возвращаем список ID найденных файлов
        Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null);
    }

    public class SearcherService : ISearcherService
    {
        // Определяем используемую версию Lucene
        // LUCENE_CURRENT или LATEST обычно рекомендуются для .NET Core/.NET 8+
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_CURRENT;
        private readonly Directory _indexDirectory;
        private readonly ILogger<SearcherService> _logger;
        private readonly string _indexPath; // Храним путь для логгирования

        public SearcherService(IConfiguration configuration, ILogger<SearcherService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _indexPath = configuration["LuceneIndexPath"] ?? "lucene_index"; // Путь из env или по умолчанию
            _logger.LogInformation("SearcherService attempting to use Lucene Index at: {IndexPath}", _indexPath);

            // Пытаемся открыть директорию при инициализации, но поиск будет проверять заново
            try
            {
                if (!System.IO.Directory.Exists(_indexPath))
                {
                    // Если папки нет при запуске, это не обязательно ошибка, она может быть создана IndexService
                    _logger.LogWarning("Lucene index directory does not exist at startup: {IndexPath}. It might be created later.", _indexPath);
                    // _indexDirectory остается null, поиск вернет пустой результат
                }
                else
                {
                    _indexDirectory = FSDirectory.Open(_indexPath);
                    _logger.LogInformation("Successfully opened Lucene index directory for reading: {IndexPath}", _indexPath);
                    // Проверка на блокировку (на всякий случай)
                    if (IndexWriter.IsLocked(_indexDirectory))
                    {
                        _logger.LogWarning("Lucene index directory is locked: {IndexPath}. Unlocking...", _indexPath);
                        IndexWriter.Unlock(_indexDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Lucene index directory at startup: {IndexPath}. Search will likely fail.", _indexPath);
                _indexDirectory = null; // Устанавливаем в null при ошибке
            }
        }

        public Task<IEnumerable<Guid>> SearchFilesAsync(string searchTerm, int? userIdFilter = null)
        {
            _logger.LogInformation("--- SearcherService: Starting search. Term='{SearchTerm}', UserFilter={UserIdFilter}", searchTerm, userIdFilter?.ToString() ?? "None");

            // Проверяем базовые условия
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogWarning("Search term is empty or whitespace. Returning empty results.");
                return Task.FromResult(Enumerable.Empty<Guid>());
            }

            // Проверяем, смогли ли мы открыть директорию индекса при старте или сейчас
            Directory currentDirectory = null;
            DirectoryReader reader = null;
            try
            {
                // Попытка открыть директорию снова, если она не была открыта при старте
                if (_indexDirectory == null)
                {
                    if (!System.IO.Directory.Exists(_indexPath))
                    {
                        _logger.LogWarning("Search aborted: Index directory does not exist at {IndexPath}", _indexPath);
                        return Task.FromResult(Enumerable.Empty<Guid>());
                    }
                    currentDirectory = FSDirectory.Open(_indexPath);
                    _logger.LogInformation("Opened index directory on demand: {IndexPath}", _indexPath);
                }
                else
                {
                    currentDirectory = _indexDirectory; // Используем открытую при старте
                }

                // Проверяем, существует ли индекс в директории
                if (!DirectoryReader.IndexExists(currentDirectory))
                {
                    _logger.LogWarning("Search aborted: No Lucene index found in directory {IndexPath}", _indexPath);
                    if (currentDirectory != _indexDirectory) currentDirectory.Dispose(); // Закрываем, если открыли только что
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                // Открываем IndexReader
                reader = DirectoryReader.Open(currentDirectory);
                var searcher = new IndexSearcher(reader);
                var analyzer = new StandardAnalyzer(AppLuceneVersion); // Используем тот же анализатор, что и при индексации
                var mainQueryParser = new QueryParser(AppLuceneVersion, "content", analyzer); // Поле для поиска по умолчанию - "content"
                mainQueryParser.AllowLeadingWildcard = false; // Запрещаем * в начале запроса (дорого)

                Query finalQuery = null;

                _logger.LogDebug("--- SearcherService: Parsing search term: '{SearchTerm}'", searchTerm);
                try
                {
                    // Экранируем специальные символы Lucene перед парсингом
                    string escapedTerm = QueryParserBase.Escape(searchTerm);
                    if (string.IsNullOrWhiteSpace(escapedTerm))
                    {
                        _logger.LogWarning("Search term became empty after escaping: '{OriginalTerm}'. Aborting search.", searchTerm);
                        return Task.FromResult(Enumerable.Empty<Guid>());
                    }

                    Query contentQuery = mainQueryParser.Parse(escapedTerm);
                    _logger.LogDebug("--- SearcherService: Parsed contentQuery: {QueryText}", contentQuery.ToString());
                    finalQuery = contentQuery;

                    // Добавляем фильтр по userId, если он задан
                    if (userIdFilter.HasValue)
                    {
                        _logger.LogDebug("--- SearcherService: Adding userId filter: {UserId}", userIdFilter.Value);
                        // Используем NumericRangeQuery для точного поиска по Int32Field "userId"
                        Query userQuery = NumericRangeQuery.NewInt32Range("userId", userIdFilter.Value, userIdFilter.Value, true, true);
                        // Объединяем запросы: И текст ДОЛЖЕН содержать терм, И userId ДОЛЖЕН совпадать
                        var booleanQuery = new BooleanQuery {
                             { contentQuery, Occur.MUST }, // Эквивалентно AND
                             { userQuery, Occur.MUST }
                         };
                        finalQuery = booleanQuery;
                    }
                }
                catch (ParseException parseEx)
                {
                    _logger.LogError(parseEx, "--- SearcherService: Error PARSING Lucene query for term: '{SearchTerm}'", searchTerm);
                    // Возвращаем пустой результат при ошибке парсинга
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }
                catch (Exception ex) // Ловим другие возможные ошибки парсера/экранирования
                {
                    _logger.LogError(ex, "--- SearcherService: Unexpected error during query parsing/building for term: '{SearchTerm}'", searchTerm);
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                if (finalQuery == null)
                {
                    _logger.LogError("--- SearcherService: Final query is null after parsing term '{SearchTerm}'. Cannot search.", searchTerm);
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                _logger.LogInformation("--- SearcherService: Executing Lucene search. Final Query: {Query}, UserFilter: {UserFilter}", finalQuery.ToString(), userIdFilter?.ToString() ?? "None");
                TopDocs topDocs = null;
                int totalHits = 0;
                var results = new List<Guid>();

                try
                {
                    // Выполняем поиск, запрашиваем топ N результатов
                    int maxResults = 100; // Ограничиваем количество возвращаемых результатов
                    topDocs = searcher.Search(finalQuery, maxResults);
                    totalHits = topDocs.TotalHits; // Общее количество совпадений
                    _logger.LogInformation("--- SearcherService: Search found {HitCount} total hits. Returning top {MaxResults}.", totalHits, maxResults);

                    // Собираем ID файлов из найденных документов
                    foreach (var scoreDoc in topDocs.ScoreDocs)
                    {
                        Document doc = searcher.Doc(scoreDoc.Doc); // Получаем документ по его внутреннему ID
                        string fileIdString = doc.Get("fileId"); // Получаем значение поля fileId
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
                    _logger.LogError(searchEx, "--- SearcherService: Error EXECUTING Lucene search. Query: {Query}", finalQuery.ToString());
                    // Возвращаем пустой результат при ошибке поиска
                    return Task.FromResult(Enumerable.Empty<Guid>());
                }

                return Task.FromResult<IEnumerable<Guid>>(results);
            }
            catch (IndexNotFoundException indexEx) // Индекс не найден
            {
                _logger.LogWarning(indexEx, "Lucene index not found at {IndexPath}", _indexPath);
                return Task.FromResult(Enumerable.Empty<Guid>());
            }
            catch (IOException ioEx) // Ошибка чтения/доступа к файлам индекса
            {
                _logger.LogError(ioEx, "IO error opening or reading Lucene index at {IndexPath}", _indexPath);
                return Task.FromResult(Enumerable.Empty<Guid>()); // Возвращаем пусто, т.к. поиск невозможен
            }
            catch (Exception ex) // Другие непредвиденные ошибки
            {
                _logger.LogError(ex, "Unexpected error during search for term: {SearchTerm}", searchTerm);
                return Task.FromResult(Enumerable.Empty<Guid>());
            }
            finally
            {
                // Гарантированно закрываем IndexReader
                reader?.Dispose();
                _logger.LogDebug("--- SearcherService: IndexReader disposed.");
                // Закрываем директорию, если открывали ее в этом методе
                if (currentDirectory != null && currentDirectory != _indexDirectory)
                {
                    currentDirectory.Dispose();
                    _logger.LogDebug("--- SearcherService: On-demand index directory disposed.");
                }
            }
        }

        // Метод Dispose для директории, открытой в конструкторе (если она была открыта)
        // Можно реализовать IDisposable для SearcherService, если это необходимо
        // public void Dispose()
        // {
        //     _indexDirectory?.Dispose();
        // }

    } // --- Конец класса SearcherService ---
} // --- Конец namespace ---