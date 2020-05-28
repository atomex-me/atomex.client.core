using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Serilog;

namespace Atomex.Common
{
    public class HttpRequestHeaders : List<KeyValuePair<string, IEnumerable<string>>> { };

    public static class HttpHelper
    {
        public const int SslHandshakeFailed = 525;
        public static HttpClient HttpClient { get; } = new HttpClient();

        public static Task<T> GetAsync<T>(
            string baseUri,
            string requestUri,
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return GetAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                headers: null,
                responseHandler: responseHandler,
                cancellationToken: cancellationToken);
        }

        public static Task<T> GetAsync<T>(
            string baseUri,
            string requestUri,
            HttpRequestHeaders headers,
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return SendRequestAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                headers: headers,
                responseHandler: responseHandler,
                cancellationToken: cancellationToken);
        }

        public static Task<Result<T>> GetAsyncResult<T>(
            string baseUri,
            string requestUri,
            Func<HttpResponseMessage, string, Result<T>> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return GetAsyncResult(
                baseUri: baseUri,
                requestUri: requestUri,
                headers: null,
                responseHandler: responseHandler,
                cancellationToken: cancellationToken);
        }

        public static Task<Result<T>> GetAsyncResult<T>(
            string baseUri,
            string requestUri,
            HttpRequestHeaders headers,
            Func<HttpResponseMessage, string, Result<T>> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return GetAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                headers: headers,
                responseHandler: response =>
                {
                    var responseContent = response.Content
                        .ReadAsStringAsync()
                        .WaitForResult();

                    return !response.IsSuccessStatusCode
                        ? new Error((int)response.StatusCode, responseContent)
                        : responseHandler(response, responseContent);
                },
                cancellationToken: cancellationToken);
        }

        public static Task<T> PostAsync<T>(
            string baseUri,
            string requestUri,
            HttpContent content,
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return SendRequestAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                method: HttpMethod.Post,
                content: content,
                headers: null,
                responseHandler: responseHandler,
                cancellationToken: cancellationToken);
        }

        public static Task<Result<T>> PostAsyncResult<T>(
            string baseUri,
            string requestUri,
            HttpContent content,
            Func<HttpResponseMessage, string, Result<T>> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return PostAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                content: content,
                responseHandler: response =>
                {
                    var responseContent = response.Content
                        .ReadAsStringAsync()
                        .WaitForResult();

                    return !response.IsSuccessStatusCode
                        ? new Error((int)response.StatusCode, responseContent)
                        : responseHandler(response, responseContent);
                },
                cancellationToken: cancellationToken);
        }

        private static async Task<T> SendRequestAsync<T>(
            string baseUri,
            string requestUri,
            HttpMethod method,
            HttpContent content,
            HttpRequestHeaders headers,
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Send {@method} request: {@baseUri}{@request}", 
                method.ToString(),
                baseUri,
                requestUri);

            try
            {
                using var response = await SendRequest(
                        baseUri: baseUri, 
                        requestUri: requestUri,
                        method: method,
                        content: content, 
                        headers: headers,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                Log.Debug(
                    response.IsSuccessStatusCode ? "Success status code: {@code}" : "Http error code: {@code}",
                    response.StatusCode);

                return responseHandler(response);
            }
            catch (HttpRequestException e)
            {
                Log.Error("SendRequestAsync error: {@message}", e.Message);
            }
            catch (Exception e)
            {
                Log.Error($"SendRequestAsync error {e.ToString()}");
            }

            return default;
        }

        public static async Task<HttpResponseMessage> SendRequest(
            string baseUri,
            string requestUri,
            HttpMethod method,
            HttpContent content,
            HttpRequestHeaders headers,
            CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(method, new Uri($"{baseUri}{requestUri}"));

            if (headers != null)
                foreach (var header in headers)
                    request.Headers.Add(header.Key, header.Value);

            if (method == HttpMethod.Post)
                request.Content = content;

            return await HttpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}