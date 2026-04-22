using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace Shinystrap.Handlers.Web;

/// <summary>
/// Represents an HTTP Handler for making requests to different endpoints.
/// </summary>
public class HttpHandler : IDisposable
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the HttpHandler class with optional proxy configuration.
    /// </summary>
    /// <param name="proxyHost">The host address of the proxy, if any.</param>
    /// <param name="proxyPort">The port number of the proxy, if any.</param>
    /// <param name="proxyUser"></param>
    /// <param name="proxyPass"></param>
    public HttpHandler(string? proxyHost = "", int proxyPort = 0, string? proxyUser = "", string? proxyPass = "")
    {
        var clientHandler = GetConfiguredClientHandler(proxyHost, proxyPort, proxyUser, proxyPass);
        _client = new HttpClient(clientHandler, true);
    }

    private HttpClientHandler GetConfiguredClientHandler(string? proxyHost, int proxyPort, string? proxyUser, string? proxyPassword)
    {
        var clientHandler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression =
                DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true 
        };
        
        if (string.IsNullOrEmpty(proxyHost) || proxyPort <= 0) return clientHandler;
        
        Console.WriteLine($"Host: {proxyHost}, Port: {proxyPort}");
        
        var proxy = new WebProxy(proxyHost, proxyPort)
        {
            UseDefaultCredentials = false
        };
        
        if (!string.IsNullOrEmpty(proxyUser) && !string.IsNullOrEmpty(proxyPassword))
        {
            Console.WriteLine("This shouldn't execute!");
            proxy.Credentials = new NetworkCredential(proxyUser, proxyPassword);
        }

        clientHandler.Proxy = proxy;
        clientHandler.UseProxy = true;

        return clientHandler;
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
    
    public async Task<HttpResponseMessage?> SendAsync(string uri, HttpMethod method, string json)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Content = new StringContent(json)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
        };

        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine($"Request timed out: {uri}");
            return null;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"IO error: {ex.Message}, retrying once...");
            await Task.Delay(150);
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
        }
    }

    
    /// <summary>
    /// Sends a HTTP request asynchronously to the specified URI using a specified HTTP method.
    /// </summary>
    /// <param name="uri">The URI to send the request to.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="requestHeaders">An array of RequestHeadersEx objects representing custom headers for the request.</param>
    /// <returns>A task representing the asynchronous operation, with a result containing the HttpResponseMessage.</returns>
    public async Task<HttpResponseMessage> SendAsync(string uri, HttpMethod method,
        RequestHeadersEx[] requestHeaders)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(uri),
            Method = method
        };

        foreach (var requestHeader in requestHeaders)
        {
            request.Headers.TryAddWithoutValidation(requestHeader.Key, requestHeader.Value);
        }
        
        var response = await _client.SendAsync(request);
        
        return response;
       
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
    
    public record RequestHeadersEx(string Key, string Value);

    public void Dispose()
    {
        _client.Dispose();
    }
}