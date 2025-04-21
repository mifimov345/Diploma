using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.IO;
using System;
using System.Text;
using System.Net;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace BackendGateway.Controllers
{
    [ApiController]
    [Route("api/{service}")]
    public class GatewayController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<GatewayController> _logger;

        public GatewayController(
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            ILogger<GatewayController> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")]
        [Route("")]
        public async Task<IActionResult> ForwardRootRequest([FromRoute] string service)
        {
            //_logger.LogInformation(">>> Forwarding ROOT request for service: {Service}", service);
            return await ForwardRequestInternal(service, null, HttpContext.Request.QueryString);
        }


        [Route("{*downstreamPath}")]
        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")]
        public async Task<IActionResult> ForwardSubPathRequest([FromRoute] string service, [FromRoute] string downstreamPath)
        {
            //_logger.LogInformation(">>> Forwarding SUBPATH request for service: {Service}, path: {DownstreamPath}", service, downstreamPath);
            return await ForwardRequestInternal(service, downstreamPath, HttpContext.Request.QueryString); // Передаем QueryString
        }

        private async Task<IActionResult> ForwardRequestInternal(string service, string? downstreamPath, QueryString queryString)
        {
            var originalRequest = _httpContextAccessor.HttpContext?.Request;
            if (originalRequest == null)
            {
                _logger.LogError("ForwardRequestInternal: HttpContext or Request is null.");
                return StatusCode(StatusCodes.Status500InternalServerError, "HttpContext is not available.");
            }

            var cancellationToken = HttpContext.RequestAborted;
            var requestPath = originalRequest.Path.Value ?? ""; // Для логов и проверки

            // Формируем URL к нижележащему сервису, передавая QueryString
            string? targetUrl = BuildTargetUrl(service, downstreamPath, queryString);

            if (targetUrl == null)
            {
                //_logger.LogWarning("ForwardRequestInternal: Target URL could not be built for service '{Service}'.", service);
                return NotFound($"Service '{service}' not found or invalid path.");
            }

            //_logger.LogInformation("--- Gateway ForwardRequestInternal ---");
            //_logger.LogInformation("Forwarding {Method} {RequestPath}{QueryString} to: {TargetUrl}",
            //    originalRequest.Method, requestPath, queryString.Value ?? "", targetUrl);

            var client = _httpClientFactory.CreateClient();
            using var requestMessage = new HttpRequestMessage();
            ConfigureHttpRequestMessage(requestMessage, originalRequest, targetUrl);
            await CopyRequestBodyAsync(originalRequest, requestMessage);
            CopyRequestHeaders(originalRequest, requestMessage);

            if (queryString.HasValue)
            {
                //_logger.LogInformation("Adding X-Original-QueryString header: {QueryString}", queryString.ToUriComponent());
                requestMessage.Headers.TryAddWithoutValidation("X-Original-QueryString", queryString.ToUriComponent());
            }


            try
            {
                //_logger.LogDebug("Sending request to downstream service: {TargetUrl}", targetUrl);
                using HttpResponseMessage response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                //_logger.LogInformation("Received response from {TargetUrl} with status code {StatusCode} ({ReasonPhrase})", targetUrl, (int)response.StatusCode, response.ReasonPhrase);

                HttpContext.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, HttpContext.Response); // Копируем "безопасные" заголовки

                bool isFileDownload = service.Equals("file", StringComparison.OrdinalIgnoreCase)
                                    && downstreamPath != null
                                    && downstreamPath.StartsWith("download/", StringComparison.OrdinalIgnoreCase);

                if (response.IsSuccessStatusCode) // Статус 2xx
                {
                    var contentType = response.Content.Headers.ContentType?.ToString(); // Получаем Content-Type
                    //_logger.LogInformation("Downstream successful response. Content-Type: {ContentType}", contentType ?? "N/A");

                    byte[] responseData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    //_logger.LogInformation("Read downstream response body: {Length} bytes.", responseData.Length);

                    if (responseData.Length == 0 && response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength > 0)
                    {
                        //_logger.LogWarning("Downstream response body is EMPTY (0 bytes) despite Content-Length={Length} for {TargetUrl}! Returning empty.", response.Content.Headers.ContentLength, targetUrl);
                        return new EmptyResult();
                    }
                    else if (responseData.Length == 0)
                    {
                        //_logger.LogWarning("Downstream response body is EMPTY (0 bytes) for {TargetUrl}! Returning empty.", targetUrl);
                        return new EmptyResult();
                    }

                    // Возвращаем массив байт как FileContentResult.
                    // Браузер сам разберется с JSON, PDF, Image, Video на основе Content-Type и Content-Disposition (если есть)
                    //_logger.LogInformation("Returning FileContentResult with {Length} bytes, Content-Type: {ContentType}", responseData.Length, contentType ?? "N/A");
                    return File(responseData, contentType ?? "application/octet-stream");
                }
                else // Неуспешный ответ (4xx, 5xx)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    //_logger.LogWarning("Downstream service at {TargetUrl} returned status {StatusCode}. Body: '{ErrorBody}'", targetUrl, response.StatusCode, errorBody);
                    var errorContentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
                    return Content(errorBody, errorContentType);
                }
            }
            catch (HttpRequestException ex)
            {
                //_logger.LogError(ex, "HttpRequestException forwarding request to {TargetUrl}.", targetUrl);
                return StatusCode(StatusCodes.Status502BadGateway, $"Error connecting to the downstream service '{service}'. Please try again later.");
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                //_logger.LogInformation(ex, "Request was cancelled by the client while forwarding or reading response from {TargetUrl}.", targetUrl);
                return new EmptyResult();
            }
            catch (TaskCanceledException ex)
            {
                if (ex.CancellationToken == cancellationToken)
                {
                    //_logger.LogInformation(ex, "Request was cancelled by the client (TaskCanceledException) for {TargetUrl}.", targetUrl);
                    return new EmptyResult();
                }
                else
                {
                    //_logger.LogError(ex, "TaskCanceledException (likely HttpClient timeout) forwarding request to {TargetUrl}.", targetUrl);
                    return StatusCode(StatusCodes.Status504GatewayTimeout, $"The request timed out while waiting for the downstream service '{service}'.");
                }
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Unexpected gateway error while forwarding request to {TargetUrl}", targetUrl);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal gateway error occurred.");
            }
        }

        private string? BuildTargetUrl(string service, string? downstreamPath, QueryString queryString)
        {
            string serviceName = service.ToLowerInvariant();
            string? targetServiceBaseUrl = serviceName switch
            {
                "auth" => "http://authservice",
                "file" => "http://fileservice",
                "search" => "http://searchservice",
                _ => null
            };

            if (targetServiceBaseUrl == null)
            {
                //_logger.LogWarning("BuildTargetUrl: Unknown service requested in URL: {ServiceName}", service);
                return null;
            }

            string targetPath;
            if (serviceName == "search")
            {
                targetPath = $"/api/{downstreamPath?.TrimStart('/') ?? serviceName}";
            }
            else
            {
                targetPath = $"/api/{serviceName}";
                if (!string.IsNullOrEmpty(downstreamPath)) { targetPath += $"/{downstreamPath.TrimStart('/')}"; }
            }
            //_logger.LogDebug("BuildTargetUrl: Path part built: {PathPart}", targetPath);

            string targetUrl = $"{targetServiceBaseUrl}{targetPath}{queryString.ToUriComponent()}";
            //_logger.LogInformation("BuildTargetUrl Result: {TargetUrl}", targetUrl);
            return targetUrl;
        }

        private void ConfigureHttpRequestMessage(HttpRequestMessage message, HttpRequest originalRequest, string targetUrl)
        {
            message.Method = new HttpMethod(originalRequest.Method);
            message.RequestUri = new Uri(targetUrl);
            //_logger.LogDebug("Configuring HttpRequestMessage. Target URI set to: {TargetUri}", message.RequestUri.AbsoluteUri);

            try
            {
                string protocolVersion = originalRequest.Protocol;
                if (!string.IsNullOrEmpty(protocolVersion))
                {
                    if (protocolVersion.EndsWith("/2", StringComparison.OrdinalIgnoreCase) || protocolVersion.EndsWith("/2.0", StringComparison.OrdinalIgnoreCase)) message.Version = HttpVersion.Version20;
                    else if (protocolVersion.EndsWith("/1.1", StringComparison.OrdinalIgnoreCase)) message.Version = HttpVersion.Version11;
                    else if (protocolVersion.EndsWith("/1.0", StringComparison.OrdinalIgnoreCase)) message.Version = HttpVersion.Version10;
                    else { message.Version = HttpVersion.Version11; _logger.LogDebug("Defaulting HTTP version to {DefaultVersion}", message.Version); }
                }
                else { message.Version = HttpVersion.Version11; _logger.LogWarning("Request protocol empty, defaulting to HTTP/1.1"); }
            }
            catch (Exception ex) { message.Version = HttpVersion.Version11; _logger.LogError(ex, "Error setting HTTP version, defaulting to {DefaultVersion}", message.Version); }
            message.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            //_logger.LogDebug("Configured HttpRequestMessage details: Method={Method}, Version={Version}, Policy={Policy}", message.Method, message.Version, message.VersionPolicy);
        }

        private async Task CopyRequestBodyAsync(HttpRequest source, HttpRequestMessage target)
        {
            bool hasBody = (source.ContentLength.HasValue && source.ContentLength > 0) || source.Headers.TransferEncoding.Contains("chunked");
            bool methodAllowsBody = HttpMethods.IsPost(source.Method) || HttpMethods.IsPut(source.Method) || HttpMethods.IsPatch(source.Method);
            if (!hasBody || !methodAllowsBody) { _logger.LogDebug("Request body not copied (Method: {Method}, HasBody: {HasBody})", source.Method, hasBody); return; }

            //_logger.LogDebug("Attempting to copy request body...");
            try
            {
                if (!source.Body.CanRead) { _logger.LogWarning("Source request body not readable."); return; }
                if (source.Body.CanSeek) { source.Body.Seek(0, SeekOrigin.Begin); } else { _logger.LogWarning("Source request body not seekable."); }

                target.Content = new StreamContent(source.Body);

                if (!string.IsNullOrEmpty(source.ContentType)) { try { target.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(source.ContentType); } catch (FormatException fex) { _logger.LogWarning(fex, "Failed to parse request Content-Type: {ContentType}", source.ContentType); } }
                if (source.ContentLength.HasValue) { target.Content.Headers.ContentLength = source.ContentLength.Value; }

                //_logger.LogDebug("Request body StreamContent copied. ContentType: {ContentType}, ContentLength Header: {ContentLength}", target.Content.Headers.ContentType, target.Content.Headers.ContentLength);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error copying request body stream."); }
        }

        private void CopyRequestHeaders(HttpRequest sourceRequest, HttpRequestMessage targetRequest)
        {
            foreach (var header in sourceRequest.Headers)
            {
                string key = header.Key;
                string[] values = header.Value.ToArray();
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) { continue; }

                if (!targetRequest.Headers.TryAddWithoutValidation(key, values))
                {
                    if (targetRequest.Content != null)
                    {
                        if (!targetRequest.Content.Headers.TryAddWithoutValidation(key, values))
                        { _logger.LogWarning("Could not add request header {HeaderKey} to request or content.", key); }
                    }
                    else { _logger.LogDebug("Could not add header {HeaderKey} (no content).", key); }
                }
            }
            if (sourceRequest.Headers.ContainsKey("Authorization")) _logger.LogDebug("Authorization header forwarded.");
            else _logger.LogDebug("No Authorization header found.");
        }

        private void CopyResponseHeaders(HttpResponseMessage sourceResponse, HttpResponse targetResponse)
        {
            targetResponse.Headers.Clear();
            foreach (var header in sourceResponse.Headers.Concat(sourceResponse.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
            {
                string key = header.Key;
                string[] values = header.Value.ToArray();
                if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) { continue; }

                //_logger.LogDebug("Copying response header: {HeaderKey} = {HeaderValue}", key, string.Join(";", values));
                try { targetResponse.Headers.AppendList(key, values.ToList()); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not append response header '{HeaderKey}'", key); }
            }
        }

        private string GetDefaultContentType(string requestPath)
        {
            if (requestPath != null && requestPath.Contains("/api/", StringComparison.OrdinalIgnoreCase))
                return System.Net.Mime.MediaTypeNames.Application.Json;
            return System.Net.Mime.MediaTypeNames.Text.Plain;
        }

    }

    public class StreamResult : IActionResult
    {
        private readonly Stream _stream;
        private readonly string? _contentType;

        public StreamResult(Stream stream, string? contentType)
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
            //logger?.LogDebug("StreamResult: Executing. ContentType: {ContentType}. Stream CanRead: {CanRead}", _contentType ?? "N/A", _stream?.CanRead ?? false);

            if (_stream == null || !_stream.CanRead)
            {
                logger?.LogError("StreamResult: Input stream is null or not readable.");
                try { context.HttpContext.Abort(); } catch { /* ignore */ }
                return;
            }


            try
            {
                logger?.LogDebug("StreamResult: Starting to copy response stream...");
                await _stream.CopyToAsync(response.Body, cancellationToken);
                logger?.LogDebug("StreamResult: Finished copying response stream.");
                await response.Body.FlushAsync(cancellationToken);
                logger?.LogDebug("StreamResult: Response body flushed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { logger?.LogInformation("StreamResult: Stream copy cancelled by client."); }
            catch (Exception ex)
            {
                logger?.LogError(ex, "StreamResult: Error copying response stream to the client.");
                try { if (!context.HttpContext.Response.HasStarted) context.HttpContext.Abort(); } catch { /* ignore */ }
            }
            finally
            {
                if (_stream != null)
                {
                    await _stream.DisposeAsync();
                    logger?.LogDebug("StreamResult: Downstream response stream disposed.");
                }
            }
        }
    }

}