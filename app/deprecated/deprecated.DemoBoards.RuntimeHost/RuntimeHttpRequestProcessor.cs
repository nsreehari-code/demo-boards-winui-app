using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DemoBoards.RuntimeHost;

internal sealed class RuntimeHttpRequestProcessor
{
    private readonly Func<string, string, string?, IReadOnlyDictionary<string, string>?, Task<(int StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)>> proxyRuntimeApiAsync;
    private readonly Func<string, Task> addCardAsync;

    public RuntimeHttpRequestProcessor(
        Func<string, string, string?, IReadOnlyDictionary<string, string>?, Task<(int StatusCode, string Body, IReadOnlyDictionary<string, string> Headers)>> proxyRuntimeApiAsync,
        Func<string, Task> addCardAsync)
    {
        this.proxyRuntimeApiAsync = proxyRuntimeApiAsync;
        this.addCardAsync = addCardAsync;
    }

    public async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string pathAndQuery = context.Request.Url?.PathAndQuery ?? path;
            if (context.Request.HttpMethod == "GET" && path == "/healthz")
            {
                await WriteJsonAsync(context.Response, 200, "{\"ok\":true,\"status\":\"ok\",\"boards\":[]}").ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "GET" && (path == "/mcp" || path == "/mcp-raw"))
            {
                await WriteJsonAsync(context.Response, 200, "{\"status\":\"ok\",\"surface\":\"agentface\",\"transport\":\"localhost-http\"}").ConfigureAwait(false);
                return;
            }

            if (ShouldProxyRequest(path, context.Request.HttpMethod))
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Dictionary<string, string> requestHeaders = new(StringComparer.OrdinalIgnoreCase);
                foreach (string? key in context.Request.Headers.AllKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        requestHeaders[key] = context.Request.Headers[key] ?? string.Empty;
                    }
                }

                (int statusCode, string responseBody, IReadOnlyDictionary<string, string> headers) = await proxyRuntimeApiAsync(context.Request.HttpMethod, pathAndQuery, body, requestHeaders).ConfigureAwait(false);
                await WriteResponseAsync(context.Response, statusCode, string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody, headers).ConfigureAwait(false);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/board/cards")
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    await WriteJsonAsync(context.Response, 400, "{\"error\":\"card body required\"}").ConfigureAwait(false);
                    return;
                }

                await addCardAsync(body).ConfigureAwait(false);
                string response = "{\"status\":\"accepted\",\"surface\":\"agentface\"}";
                await WriteJsonAsync(context.Response, 200, response).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context.Response, 404, "{\"error\":\"not-found\"}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, 500, "{\"error\":\"" + EscapeJson(ex.Message) + "\"}").ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static bool ShouldProxyRequest(string path, string? httpMethod)
    {
        string method = httpMethod?.ToUpperInvariant() ?? "GET";
        if (path.StartsWith("/api/boards/", StringComparison.Ordinal))
        {
            return method is "GET" or "POST" or "PATCH" or "PUT" or "DELETE";
        }

        return path is "/mcp"
            or "/mcp-raw"
            or "/mcp-actions"
            or "/mcp-controlplane"
            or "/mcp-webhooks"
            or "/mcp-extras"
            or "/manage-boards"
            or "/agent/mcp"
            or "/agent/mcp/manifest";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, string payload)
    {
        await WriteResponseAsync(response, statusCode, payload, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["content-type"] = "application/json; charset=utf-8"
        }).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, int statusCode, string payload, IReadOnlyDictionary<string, string>? headers)
    {
        response.StatusCode = statusCode;

        string contentType = "application/json; charset=utf-8";
        if (headers is not null)
        {
            foreach ((string key, string value) in headers)
            {
                if (string.Equals(key, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                }
                else if (!string.Equals(key, "content-length", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    response.Headers[key] = value;
                }
            }
        }

        response.ContentType = contentType;
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private static string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
