using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace Shinystrap.Handlers.Web;

/// <summary>
/// Represents an HTTP Handler for making requests to different endpoints.
/// </summary>
public sealed class HttpHandler : IDisposable
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the HttpHandler class with optional proxy configuration.
    /// </summary>
    public HttpHandler()
    {
        var clientHandler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression =
                DecompressionMethods.All
        };
        
        _client = new HttpClient(clientHandler, true);
    }

    /// <summary>
    /// Sends a HTTP request asynchronously to the specified URI using a specified HTTP method.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <returns>A task representing the asynchronous operation, with a result containing the HttpResponseMessage.</returns>
    public async Task<HttpResponseMessage> SendAsync(string uri, HttpMethod method)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(uri),
            Method = method,
        };
        
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Sends a HTTP request asynchronously to the specified URI using a specified HTTP method.
    /// </summary>
    /// <param name="url">The URL to get the content from.</param>
    /// <returns>A task representing the asynchronous operation, with a result containing the content as a string.</returns>
    public async Task<string> GetStringAsync(string url)
        => await _client.GetStringAsync(url);
    
    public async Task DownloadFileAsync(string? url, string path)
    {
        var tempPath = Path.GetTempPath();
        var fullPath = Path.Combine(tempPath, path);

        try
        {
            var streamAsync = await _client.GetStreamAsync(url);

            await using var fileStream =
                new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await streamAsync.CopyToAsync(fileStream);
        }
        catch (UnauthorizedAccessException e)
        {
            Console.WriteLine($"Error: Access denied to path: {fullPath}. Exception: {e.Message}");
        }
    }
    
    /// <summary>
    ///  Sends a HTTP request with a JSON payload asynchronously to the specified URI using a specified HTTP method.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="json">The JSON content to include in the request body.</param>
    /// <param name="requestHeaders">An array of RequestHeadersEx objects representing custom headers for the request.</param>
    /// <returns>A task representing the asynchronous operation, with a result containing the HttpResponseMessage.</returns>
    public async Task<HttpResponseMessage> SendAsync(string uri, HttpMethod method,
        string json, RequestHeadersEx[] requestHeaders)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(uri),
            Method = method,
            Content = new StringContent(json)
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("application/json")
                }
            },
        };


        foreach (var requestHeader in requestHeaders)
        {
            request.Headers.TryAddWithoutValidation(requestHeader.Key, requestHeader.Value);
        }
        
        return await _client.SendAsync(request);
    }
    
    /// <summary>
    /// Sends a HTTP request asynchronously to the specified URI using a specified HTTP method.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="requestHeaders">An array of RequestHeadersEx objects representing custom headers for the request.</param>
    /// <param name="timeout">Optional. The timespan to wait before the request times out. If not specified, HttpClient's timeout is used.</param>
    /// <returns>A task representing the asynchronous operation, with a result containing the HttpResponseMessage.</returns>
    public async Task<HttpResponseMessage> SendAsync(string uri, HttpMethod method,
        RequestHeadersEx[] requestHeaders, TimeSpan? timeout = null)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(uri),
            Method = method
        };

        foreach (var requestHeader in requestHeaders)
        {
            request.Headers.Add(requestHeader.Key, requestHeader.Value);
        }

        using var cts = new CancellationTokenSource(timeout ?? _client.Timeout);
        return await _client.SendAsync(request, cts.Token);
    }
    
    public record RequestHeadersEx(string Key, string? Value);
    
    public void Dispose()
    {
        _client.Dispose();
    }
}