using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Serilog;

namespace Atomex.Common
{
    public static class HttpHelper
    {
        public static Task<T> GetAsync<T>(
            string baseUri,
            string requestUri,
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return SendRequestAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseHandler,
                cancellationToken: cancellationToken);
        }

        public static Task<Result<T>> GetAsyncResult<T>(
            string baseUri,
            string requestUri,
            Func<HttpResponseMessage, string, Result<T>> responseHandler,
            CancellationToken cancellationToken = default)
        {
            return GetAsync(
                baseUri: baseUri,
                requestUri: requestUri,
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
            Func<HttpResponseMessage, T> responseHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Send {@method} request: {@baseUri}{@request}", 
                method.ToString(),
                baseUri,
                requestUri);

            try
            {
                using (var response = await SendRequest(baseUri, requestUri, method, content, cancellationToken)
                    .ConfigureAwait(false))
                {
                    Log.Debug(
                        response.IsSuccessStatusCode ? "Success status code: {@code}" : "Http error code: {@code}",
                        response.StatusCode);

                    return responseHandler(response);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "SendRequestAsync error");
            }

            return default;
        }

        public static async Task<HttpResponseMessage> SendRequest(
            string baseUri,
            string requestUri,
            HttpMethod method,
            HttpContent content,
            CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient {BaseAddress = new Uri(baseUri)})
            {
                if (method == HttpMethod.Get)
                {
                    return await httpClient
                        .GetAsync(requestUri, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (method == HttpMethod.Post)
                {
                    return await httpClient
                        .PostAsync(requestUri, content, cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new ArgumentException("Http method not supported");
            }
        }
    }
}