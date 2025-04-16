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

        public GatewayController(
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            ILogger<GatewayController> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Route("{*downstreamPath}")]
        [AcceptVerbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")]
        public async Task<IActionResult> ForwardRequest([FromRoute] string service, [FromRoute] string downstreamPath)
        {
            var originalRequest = _httpContextAccessor.HttpContext.Request;
            var cancellationToken = HttpContext.RequestAborted;
            var requestPath = originalRequest.Path.Value ?? "";
            var isFileDownloadRequest = requestPath.Contains("/download/", StringComparison.OrdinalIgnoreCase); // Определяем запрос на скачивание

            _logger.LogInformation("--- Gateway ForwardRequest ---");
            _logger.LogInformation("Route parameter 'service': {ServiceParam}", service);
            _logger.LogInformation("Route parameter 'downstreamPath': {PathParam}", downstreamPath ?? "<null_or_empty>");
            _logger.LogInformation("Original Request Path: {OriginalPath}", requestPath);
            _logger.LogInformation("Original Request Query: {OriginalQuery}", originalRequest.QueryString);

            string serviceName = service;

            // Определяем базовый URL целевого сервиса
            string targetServiceBaseUrl = serviceName.ToLower() switch
            {
                "auth" => "http://authservice",    // Имя сервиса из docker-compose
                "file" => "http://fileservice",    // Имя сервиса из docker-compose
                _ => null
            };

            if (targetServiceBaseUrl == null)
            {
                _logger.LogWarning("Unknown service requested: {ServiceName}", serviceName);
                return NotFound($"Service '{serviceName}' not found.");
            }

            string targetUrl = $"{targetServiceBaseUrl}/api/{serviceName}";
            if (!string.IsNullOrEmpty(downstreamPath))
            {
                targetUrl += $"/{downstreamPath.TrimStart('/')}";
            }
            targetUrl += originalRequest.QueryString;

            _logger.LogInformation("Forwarding {Method} request to: {TargetUrl}", originalRequest.Method, targetUrl);

            var client = _httpClientFactory.CreateClient();
            using var requestMessage = new HttpRequestMessage();
            requestMessage.Method = new HttpMethod(originalRequest.Method);
            requestMessage.RequestUri = new Uri(targetUrl);

            await SetRequestContentAsync(originalRequest, requestMessage);
            CopyRequestHeaders(originalRequest, requestMessage);

            try
            {
                using HttpResponseMessage response = await client.SendAsync(requestMessage,
                    isFileDownloadRequest ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    cancellationToken);

                _logger.LogInformation("Received response from {TargetUrl} with status code {StatusCode}", targetUrl, response.StatusCode);

                HttpContext.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, HttpContext.Response);

                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Downstream service at {TargetUrl} returned status {StatusCode}. Body: '{ErrorBody}'", targetUrl, response.StatusCode, errorBody);
                    return Content(errorBody, response.Content.Headers.ContentType?.ToString() ?? "text/plain");
                }
                else if (isFileDownloadRequest)
                {
                    _logger.LogInformation("<<< Gateway preparing to stream file download response.");
                    var contentType = response.Content.Headers.ContentType?.ToString();
                    return new StreamResult(await response.Content.ReadAsStreamAsync(cancellationToken), contentType);
                }
                else
                {
                    _logger.LogInformation("<<< Gateway preparing to return full response content (non-download).");
                    try
                    {
                        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogInformation("<<< Read response body: {BodyLength} chars", responseBody.Length);
                        _logger.LogDebug("<<< Response Body Content: {Body}", responseBody);

                        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
                        return Content(responseBody, contentType);
                    }
                    catch (Exception exRead)
                    {
                        _logger.LogError(exRead, "Error reading non-download response body in Gateway from {TargetUrl}", targetUrl);
                        return StatusCode(StatusCodes.Status500InternalServerError, "Error reading response body in gateway");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HttpRequestException while forwarding request to {TargetUrl}.", targetUrl);
                return StatusCode(StatusCodes.Status502BadGateway, $"Error connecting to downstream service '{serviceName}'.");
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                _logger.LogInformation(ex, "Request cancelled by client while forwarding to {TargetUrl}.", targetUrl);
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected Gateway error while forwarding request to {TargetUrl}", targetUrl);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal gateway error.");
            }
        }
        private async Task SetRequestContentAsync(HttpRequest sourceRequest, HttpRequestMessage targetRequest)
        {
            if (sourceRequest.ContentLength.HasValue && sourceRequest.ContentLength > 0 &&
                (sourceRequest.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                 sourceRequest.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                 sourceRequest.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourceRequest.Body.CanSeek)
                {
                    sourceRequest.Body.Seek(0, SeekOrigin.Begin);
                    _logger.LogDebug("Request body seeked to beginning before copying.");
                }
                else
                {
                    _logger.LogWarning("Request body is not seekable for copy. Ensure buffering is enabled in Program.cs.");
                }

                var memoryStream = new MemoryStream();
                await sourceRequest.Body.CopyToAsync(memoryStream, sourceRequest.HttpContext.RequestAborted);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var streamContent = new StreamContent(memoryStream);
                targetRequest.Content = streamContent;

                if (!string.IsNullOrEmpty(sourceRequest.ContentType))
                {
                    try { streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(sourceRequest.ContentType); }
                    catch (FormatException ex) { _logger.LogWarning(ex, "Failed to parse request Content-Type: {ContentType}", sourceRequest.ContentType); }
                }
                _logger.LogDebug("Request body copied to target message using MemoryStream.");
            }
            else
            {
                _logger.LogDebug("No request body to copy (Method: {Method}, ContentLength: {Length}).", sourceRequest.Method, sourceRequest.ContentLength);
            }
        }

        private void CopyRequestHeaders(HttpRequest sourceRequest, HttpRequestMessage targetRequest)
        {
            foreach (var header in sourceRequest.Headers)
            {
                string key = header.Key;
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Connection", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Trailers", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!targetRequest.Headers.TryAddWithoutValidation(key, header.Value.ToArray()))
                {
                    if (targetRequest.Content != null)
                    {
                        if (!targetRequest.Content.Headers.TryAddWithoutValidation(key, header.Value.ToArray()))
                        {
                            _logger.LogWarning("Could not add request header {HeaderKey} to request or content.", key);
                        }
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
            foreach (var header in sourceResponse.Headers.Concat(sourceResponse.Content.Headers))
            {
                string key = header.Key;
                if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || 
                    key.StartsWith("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Trailers", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping response header copy: {HeaderKey}", key);
                    continue;
                }

                _logger.LogDebug("Copying response header: {HeaderKey} = [{HeaderValue}]", key, string.Join(", ", header.Value));
                if (!targetResponse.Headers.TryAdd(key, header.Value.ToArray()))
                {
                    _logger.LogWarning("Could not add response header '{HeaderKey}' to outgoing response.", key);
                }
            }
        }
    }

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

            if (!string.IsNullOrEmpty(_contentType))
            {
                response.ContentType = _contentType;
                logger?.LogDebug("StreamResult: Setting response Content-Type to {ContentType}", _contentType);
            }
            else
            {
                logger?.LogDebug("StreamResult: Response Content-Type is not set.");
            }

            try
            {
                logger?.LogDebug("StreamResult: Starting to copy stream...");
                await _stream.CopyToAsync(response.Body, cancellationToken);
                logger?.LogDebug("StreamResult: Finished copying stream.");
                await response.Body.FlushAsync(cancellationToken);
                logger?.LogDebug("StreamResult: Response body flushed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger?.LogInformation("StreamResult: Stream copy cancelled by client.");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "StreamResult: Error copying response stream.");
                if (!context.HttpContext.Response.HasStarted)
                {
                    response.StatusCode = StatusCodes.Status500InternalServerError;
                }
                context.HttpContext.Abort();
            }
            finally
            {
                await _stream.DisposeAsync();
                logger?.LogDebug("StreamResult: Downstream response stream disposed.");
            }
        }
    }
}