using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers; // Для MediaTypeHeaderValue и AuthenticationHeaderValue
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging; // Для логирования
using System.Linq; // Для Concat
using System.IO;   // Для SeekOrigin, MemoryStream
using System;     // Для Uri, StringComparison, ArgumentNullException
using System.Text;
using System.Net; // Для Encoding

namespace BackendGateway.Controllers
{
    [ApiController]
    // Базовый маршрут контроллера: ловит /api/{service}
    [Route("api/{service}")]
    public class GatewayController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<GatewayController> _logger;

        // Внедряем зависимости через конструктор
        public GatewayController(
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            ILogger<GatewayController> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Ловит все HTTP-методы для путей ПОСЛЕ /api/{service}/
        // Весь остальной путь (включая слеши) попадает в 'downstreamPath'
        [Route("{*downstreamPath}")]
        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")]
        public async Task<IActionResult> ForwardRequest([FromRoute] string service, [FromRoute] string downstreamPath)
        {
            var originalRequest = _httpContextAccessor.HttpContext.Request;
            var cancellationToken = HttpContext.RequestAborted; // Получаем токен отмены
            var requestPath = originalRequest.Path.Value ?? ""; // Получаем оригинальный путь запроса

            // Формируем URL к нижележащему сервису
            string targetUrl = BuildTargetUrl(service, downstreamPath, originalRequest.Query); // Передаем IQueryCollection

            if (targetUrl == null)
            {
                _logger.LogWarning("Target URL could not be built for service '{Service}'.", service);
                return NotFound($"Service '{service}' not found or invalid path.");
            }

            _logger.LogInformation("--- Gateway ForwardRequest ---");
            _logger.LogInformation("Forwarding {Method} {RequestPath} to: {TargetUrl}", originalRequest.Method, requestPath, targetUrl);

            var client = _httpClientFactory.CreateClient();
            using var requestMessage = new HttpRequestMessage();
            ConfigureHttpRequestMessage(requestMessage, originalRequest, targetUrl); // Настройка метода, URI, версии
            await CopyRequestBodyAsync(originalRequest, requestMessage); // Копирование тела запроса
            CopyRequestHeaders(originalRequest, requestMessage); // Копирование заголовков запроса

            try
            {
                // --- ВАЖНО: Используем ResponseHeadersRead для возможности потоковой передачи файлов ---
                _logger.LogDebug("Sending request to downstream service...");
                using HttpResponseMessage response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                _logger.LogInformation("Received response from {TargetUrl} with status code {StatusCode} ({ReasonPhrase})", targetUrl, (int)response.StatusCode, response.ReasonPhrase);

                // Копируем статус-код и заголовки ответа в исходящий ответ
                HttpContext.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, HttpContext.Response); // Копируем "безопасные" заголовки

                // --- Определяем, как вернуть ответ ---
                // 1. Скачивание/Предпросмотр файлов? (Проверяем по пути - /api/file/download/*)
                bool isFileDownload = service.Equals("file", StringComparison.OrdinalIgnoreCase)
                                    && downstreamPath != null
                                    && downstreamPath.StartsWith("download/", StringComparison.OrdinalIgnoreCase);

                // 2. Успешный ответ?
                if (response.IsSuccessStatusCode) // Статус 2xx
                {
                    if (isFileDownload)
                    {
                        // Возвращаем файл потоком для эффективности
                        _logger.LogInformation("<<< Gateway preparing to stream file download response.");
                        var contentType = response.Content.Headers.ContentType?.ToString();
                        return new StreamResult(await response.Content.ReadAsStreamAsync(cancellationToken), contentType);
                    }
                    else // Обычный успешный ответ (JSON, text и т.д.)
                    {
                        // Читаем тело как строку и возвращаем через ContentResult
                        _logger.LogInformation("<<< Gateway preparing to return full response content (non-download).");
                        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogDebug("<<< Read response body: {Length} chars. Body Sample: {BodySample}", responseBody.Length, responseBody.Substring(0, Math.Min(responseBody.Length, 200)));
                        var contentType = response.Content.Headers.ContentType?.ToString() ?? GetDefaultContentType(requestPath);
                        // Устанавливаем Content-Type явно, т.к. Content() не всегда его правильно угадывает
                        return Content(responseBody, contentType);
                    }
                }
                else // Неуспешный ответ (4xx, 5xx)
                {
                    // Пытаемся прочитать тело ошибки и вернуть его как есть
                    string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Downstream service at {TargetUrl} returned status {StatusCode}. Body: '{ErrorBody}'", targetUrl, response.StatusCode, errorBody);
                    var errorContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
                    // Статус код уже установлен из response.StatusCode
                    return Content(errorBody, errorContentType);
                }
            }
            catch (HttpRequestException ex) // Ошибки сети/подключения/DNS
            {
                _logger.LogError(ex, "HttpRequestException forwarding request to {TargetUrl}. Downstream service might be unavailable. Message: {ErrorMessage}", targetUrl, ex.Message);
                // Возвращаем 502 Bad Gateway
                return StatusCode(StatusCodes.Status502BadGateway, $"Error connecting to the downstream service '{service}'. Please try again later.");
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken) // Отмена запроса клиентом
            {
                _logger.LogInformation(ex, "Request was cancelled by the client while forwarding or reading response from {TargetUrl}.", targetUrl);
                // Клиент уже отсоединился, просто возвращаем пустой результат
                return new EmptyResult();
            }
            catch (TaskCanceledException ex) // Таймаут HttpClient или другая отмена задачи
            {
                if (ex.CancellationToken == cancellationToken)
                { // Если это отмена клиентом
                    _logger.LogInformation(ex, "Request was cancelled by the client (TaskCanceledException) for {TargetUrl}.", targetUrl);
                    return new EmptyResult();
                }
                else
                { // Скорее всего, таймаут HttpClient
                    _logger.LogError(ex, "TaskCanceledException (likely HttpClient timeout) forwarding request to {TargetUrl}.", targetUrl);
                    // Возвращаем 504 Gateway Timeout
                    return StatusCode(StatusCodes.Status504GatewayTimeout, $"The request timed out while waiting for the downstream service '{service}'.");
                }
            }
            catch (Exception ex) // Другие непредвиденные ошибки шлюза
            {
                _logger.LogError(ex, "Unexpected gateway error while forwarding request to {TargetUrl}", targetUrl);
                // Возвращаем 500 Internal Server Error
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal gateway error occurred.");
            }
        }

        // --- Вспомогательные методы ---

        private string BuildTargetUrl(string service, string downstreamPath, IQueryCollection query)
        {
            string serviceName = service.ToLowerInvariant(); // Используем имя из URL для определения сервиса
            string targetServiceBaseUrl = serviceName switch
            {
                "auth" => "http://authservice",
                "file" => "http://fileservice",
                "search" => "http://searchservice", // Добавлен сервис поиска
                _ => null // Неизвестный сервис
            };

            if (targetServiceBaseUrl == null)
            {
                _logger.LogWarning("BuildTargetUrl: Unknown service requested in URL: {ServiceName}", service); // Логируем исходное имя
                return null;
            }

            // Собираем URL: базовый URL сервиса + /api/ + имя сервиса + остальной путь + строка запроса
            string targetUrl = $"{targetServiceBaseUrl}/api/{serviceName}";
            if (!string.IsNullOrEmpty(downstreamPath))
            {
                targetUrl += $"/{downstreamPath.TrimStart('/')}";
            }
            // Формируем строку запроса из коллекции IQueryCollection
            targetUrl += QueryString.Create(query.ToDictionary(q => q.Key, q => q.Value.ToString())).ToUriComponent();

            _logger.LogDebug("BuildTargetUrl Result: {TargetUrl}", targetUrl);
            return targetUrl;
        }

        private void ConfigureHttpRequestMessage(HttpRequestMessage message, HttpRequest originalRequest, string targetUrl)
        {
            message.Method = new HttpMethod(originalRequest.Method);
            message.RequestUri = new Uri(targetUrl);

            // --- ИСПРАВЛЕНИЕ: Установка версии HTTP ---
            try
            {
                string protocolVersion = originalRequest.Protocol; // Например, "HTTP/1.1" или "HTTP/2.0"
                if (!string.IsNullOrEmpty(protocolVersion))
                {
                    if (protocolVersion.EndsWith("/2") || protocolVersion.EndsWith("/2.0"))
                    {
                        message.Version = HttpVersion.Version20; // Используем статическое свойство
                    }
                    else if (protocolVersion.EndsWith("/1.1"))
                    {
                        message.Version = HttpVersion.Version11; // Используем статическое свойство
                    }
                    else if (protocolVersion.EndsWith("/1.0"))
                    {
                        message.Version = HttpVersion.Version10; // Используем статическое свойство
                    }
                    else
                    {
                        // Если не распознали, ставим 1.1 по умолчанию
                        message.Version = HttpVersion.Version11;
                        _logger.LogDebug("Could not parse HTTP version from '{Protocol}', defaulting to {DefaultVersion}", protocolVersion, message.Version);
                    }
                }
                else
                {
                    // Если протокол пуст, ставим 1.1
                    message.Version = HttpVersion.Version11;
                    _logger.LogWarning("Original request protocol was empty, defaulting to HTTP/1.1");
                }
            }
            catch (Exception ex) // Ловим любые ошибки парсинга или доступа
            {
                message.Version = HttpVersion.Version11; // Безопасный fallback
                _logger.LogError(ex, "Error determining HTTP version from protocol '{Protocol}', defaulting to {DefaultVersion}", originalRequest.Protocol, message.Version);
            }

            // Устанавливаем политику версии (позволяем понижение)
            message.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

            _logger.LogDebug("Configured HttpRequestMessage: Method={Method}, Version={Version}, VersionPolicy={Policy}, URI={Uri}", message.Method, message.Version, message.VersionPolicy, message.RequestUri);
        }

        private async Task CopyRequestBodyAsync(HttpRequest source, HttpRequestMessage target)
        {
            // Проверяем, есть ли тело для копирования
            bool hasBody = (source.ContentLength.HasValue && source.ContentLength > 0) || source.Headers.TransferEncoding.Contains("chunked");
            bool methodAllowsBody = HttpMethods.IsPost(source.Method) || HttpMethods.IsPut(source.Method) || HttpMethods.IsPatch(source.Method);

            if (!hasBody || !methodAllowsBody)
            {
                _logger.LogDebug("Request body not copied (Method: {Method}, HasBody: {HasBody})", source.Method, hasBody);
                return;
            }

            _logger.LogDebug("Attempting to copy request body...");
            try
            {
                if (!source.Body.CanRead) { _logger.LogWarning("Source request body is not readable."); return; }
                if (source.Body.CanSeek) { source.Body.Seek(0, SeekOrigin.Begin); _logger.LogDebug("Source request body seeked to beginning."); }
                else { _logger.LogWarning("Source request body is not seekable."); }

                // Используем StreamContent без передачи владения потоком
                target.Content = new StreamContent(source.Body);

                // Копируем заголовки контента
                if (!string.IsNullOrEmpty(source.ContentType))
                {
                    try { target.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(source.ContentType); }
                    catch (FormatException fex) { _logger.LogWarning(fex, "Failed to parse request Content-Type: {ContentType}", source.ContentType); }
                }
                if (source.ContentLength.HasValue) { target.Content.Headers.ContentLength = source.ContentLength.Value; } // Копируем, если есть

                _logger.LogDebug("Request body StreamContent copied. ContentType: {ContentType}, ContentLength Header: {ContentLength}", target.Content.Headers.ContentType, target.Content.Headers.ContentLength);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error copying request body stream."); }
        }

        private void CopyRequestHeaders(HttpRequest sourceRequest, HttpRequestMessage targetRequest)
        {
            foreach (var header in sourceRequest.Headers)
            {
                string key = header.Key;
                string[] values = header.Value.ToArray();

                // Исключаем заголовки Host и те, что связаны с контентом (копируются в CopyRequestBodyAsync)
                // и hop-by-hop заголовки
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Trailers", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
                { continue; }

                // Пытаемся добавить к заголовкам запроса
                if (!targetRequest.Headers.TryAddWithoutValidation(key, values))
                {
                    // Если не вышло и есть контент, пытаемся добавить к заголовкам контента
                    if (targetRequest.Content != null)
                    {
                        if (!targetRequest.Content.Headers.TryAddWithoutValidation(key, values))
                        { _logger.LogWarning("Could not add request header {HeaderKey} to request or content.", key); }
                    }
                    else { _logger.LogDebug("Could not add request header {HeaderKey} (no content).", key); }
                }
            }
            if (sourceRequest.Headers.ContainsKey("Authorization")) _logger.LogDebug("Authorization header forwarded.");
            else _logger.LogDebug("No Authorization header found in the original request.");
        }

        private void CopyResponseHeaders(HttpResponseMessage sourceResponse, HttpResponse targetResponse)
        {
            targetResponse.Headers.Clear();
            // Копируем заголовки из ответа и из контента ответа
            foreach (var header in sourceResponse.Headers.Concat(sourceResponse.Content.Headers))
            {
                string key = header.Key;
                string[] values = header.Value.ToArray();

                // Исключаем hop-by-hop и Content-Length
                if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    // ... другие hop-by-hop ...
                    key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                { continue; }

                _logger.LogDebug("Copying response header: {HeaderKey} = {HeaderValue}", key, string.Join(";", values));
                try { targetResponse.Headers.AppendList(key, values.ToList()); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not append response header '{HeaderKey}'", key); }
            }
        }

        // Определяем Content-Type по умолчанию для не-файловых ответов
        private string GetDefaultContentType(string requestPath)
        {
            // Простое определение: если путь содержит /api/, считаем JSON
            if (requestPath != null && requestPath.Contains("/api/", StringComparison.OrdinalIgnoreCase))
                return "application/json; charset=utf-8";
            // Иначе - простой текст
            return "text/plain; charset=utf-8";
        }

    } // --- Конец класса GatewayController ---

    // Вспомогательный класс для возврата потока напрямую (для скачивания файлов)
    public class StreamResult : IActionResult
    {
        private readonly Stream _stream;
        private readonly string _contentType;

        public StreamResult(Stream stream, string contentType)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _contentType = contentType;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            var cancellationToken = context.HttpContext.RequestAborted;
            var logger = context.HttpContext.RequestServices.GetService<ILogger<StreamResult>>();

            if (!string.IsNullOrEmpty(_contentType)) { response.ContentType = _contentType; }
            logger?.LogDebug("StreamResult: Executing. ContentType: {ContentType}. Stream CanRead: {CanRead}", _contentType ?? "N/A", _stream.CanRead);

            try
            {
                // Копируем поток в тело ответа
                await _stream.CopyToAsync(response.Body, cancellationToken);
                await response.Body.FlushAsync(cancellationToken); // Принудительно сбрасываем буфер
                logger?.LogDebug("StreamResult: Stream copied and flushed successfully.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { logger?.LogInformation("StreamResult: Stream copy cancelled by client."); }
            catch (Exception ex)
            {
                logger?.LogError(ex, "StreamResult: Error copying response stream to the client.");
                // Прерываем соединение, если можем
                try { if (!context.HttpContext.Response.HasStarted) context.HttpContext.Abort(); } catch { /* ignore */ }
            }
            finally
            {
                // Гарантированно освобождаем поток, полученный от HttpClient
                await _stream.DisposeAsync();
                logger?.LogDebug("StreamResult: Downstream response stream disposed.");
            }
        }
    }

} // --- Конец namespace ---