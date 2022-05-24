using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public class HttpRequestHeaders : List<KeyValuePair<string, IEnumerable<string>>> { };

    public static class HttpHelper
    {
        public const int SslHandshakeFailed = 525;
        public static HttpClient HttpClient { get; } = new HttpClient();

        public static async Task<HttpResponseMessage> SendRequestAsync(
            string baseUri,
            string relativeUri,
            HttpMethod method,
            HttpContent content = null,
            HttpRequestHeaders headers = null,
            RequestLimitControl requestLimitControl = null,
            CancellationToken cancellationToken = default)
        {
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