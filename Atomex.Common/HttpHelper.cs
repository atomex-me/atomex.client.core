using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Atomex.Common
{
    public class HttpRequestHeaders : List<KeyValuePair<string, IEnumerable<string>>>
    {
        public HttpRequestHeaders() { }
        public HttpRequestHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> values)
        {
            AddRange(values);
        }
        public HttpRequestHeaders(IEnumerable<KeyValuePair<string, string>> values)
            : this(values.Select(pair => new KeyValuePair<string, IEnumerable<string>>(
                key: pair.Key,
                value: new string[] { pair.Value })))
        {
        }
    };

    public static class HttpHelper
    {
        public const int SslHandshakeFailed = 525;
        public static HttpClient HttpClient { get; } = new HttpClient();
        
        public static async Task<Result<T>> PostAsyncResult<T>(
            string baseUri,
            string requestUri,
            HttpContent content,
            HttpRequestHeaders headers,
            Func<HttpResponseMessage, string, Result<T>> responseHandler,
            CancellationToken cancellationToken = default)
        {
            using var response = await PostAsync(
                    baseUri: baseUri,
                    relativeUri: requestUri,
                    content: content,
                    headers: headers,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            return !response.IsSuccessStatusCode
                ? new Error
                {
                    Code = (int)response.StatusCode,
                    Message = responseContent
                }
                : responseHandler(response, responseContent);
        }

        public static async Task<HttpResponseMessage> SendRequestAsync(
            string baseUri,
            string relativeUri,
            HttpMethod method,
            HttpContent content = null,
            HttpRequestHeaders headers = null,
            RequestLimitControl requestLimitControl = null,
            ILogger logger = null,
            CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("Send {@method} request: {@baseUri}{@request}",
                method.ToString(),
                baseUri,
                relativeUri);

            var requestUri = new Uri(Url.Combine(baseUri, relativeUri));

            using var request = new HttpRequestMessage(method, requestUri);

            if (headers != null)
                foreach (var header in headers)
                    request.Headers.Add(header.Key, header.Value);

            if (method == HttpMethod.Post)
                request.Content = content;

            if (requestLimitControl != null)
                await requestLimitControl
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

            return await HttpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        public static Task<HttpResponseMessage> GetAsync(
            string baseUri,
            string relativeUri,
            HttpRequestHeaders headers = null,
            RequestLimitControl requestLimitControl = null,
            CancellationToken cancellationToken = default)
        {
            return SendRequestAsync(
                baseUri: baseUri,
                relativeUri: relativeUri,
                method: HttpMethod.Get,
                content: null,
                headers: headers,
                requestLimitControl: requestLimitControl,
                cancellationToken: cancellationToken);
        }

        public static Task<HttpResponseMessage> PostAsync(
            string baseUri,
            string relativeUri,
            HttpContent content = null,
            HttpRequestHeaders headers = null,
            RequestLimitControl requestLimitControl = null,
            CancellationToken cancellationToken = default)
        {
            return SendRequestAsync(
                baseUri: baseUri,
                relativeUri: relativeUri,
                method: HttpMethod.Post,
                content: content,
                headers: headers,
                requestLimitControl: requestLimitControl,
                cancellationToken: cancellationToken);
        }
    }
}