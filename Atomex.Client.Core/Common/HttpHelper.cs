using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Atomex.Common
{
    public static class HttpHelper
    {
        private const int HttpTooManyRequests = 429;
        private const int TooManyRequestsDelayMs = 20000;
        private const int MaxDelayMs = 1000;

        public static Task<T> GetAsync<T>(
            string baseUri,
            string requestUri,
            Func<string, T> responseHandler,
            RequestLimitChecker requestLimitChecker = null,
            int maxAttempts = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendRequestAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseHandler,
                requestLimitChecker: requestLimitChecker,
                maxAttempts: maxAttempts,
                cancellationToken: cancellationToken);
        }

        public static Task<T> PostAsync<T>(
            string baseUri,
            string requestUri,
            HttpContent content,
            Func<string, T> responseHandler,
            RequestLimitChecker requestLimitChecker = null,
            int maxAttempts = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendRequestAsync(
                baseUri: baseUri,
                requestUri: requestUri,
                method: HttpMethod.Post,
                content: content,
                responseHandler: responseHandler,
                requestLimitChecker: requestLimitChecker,
                maxAttempts: maxAttempts,
                cancellationToken: cancellationToken);
        }

        private static async Task<T> SendRequestAsync<T>(
            string baseUri,
            string requestUri,
            HttpMethod method,
            HttpContent content,
            Func<string, T> responseHandler,
            RequestLimitChecker requestLimitChecker = null,
            int maxAttempts = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tryToSend = true;
            var attempts = 0;

            while (tryToSend)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                if (requestLimitChecker != null)
                    await requestLimitChecker.WaitIfNeeded(cancellationToken)
                        .ConfigureAwait(false);

                Log.Debug("Send request: {@request}", requestUri);

                try
                {
                    attempts++;

                    using (var response = await SendRequest(baseUri, requestUri, method, content, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content
                                .ReadAsStringAsync()
                                .ConfigureAwait(false);

                            Log.Verbose($"Raw response content: {responseContent}");

                            return responseHandler(responseContent);
                        }
                        if ((int)response.StatusCode == HttpTooManyRequests)
                        {
                            Log.Debug("Too many requests");

                            for (var i = 0; i < TooManyRequestsDelayMs / MaxDelayMs; ++i)
                                await Task.Delay(MaxDelayMs, cancellationToken)
                                    .ConfigureAwait(false);

                            continue;
                        }

                        var responseText = await response.Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);

                        Log.Error("Invalid response: {@code} {@text}", response.StatusCode, responseText);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Http request error");

                    if (attempts < maxAttempts)
                        continue;
                }

                tryToSend = false;
            }

            return default(T);
        }

        public static async Task<HttpResponseMessage> SendRequest(
            string baseUri,
            string requestUri,
            HttpMethod method,
            HttpContent content,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var httpClient = new HttpClient { BaseAddress = new Uri(baseUri) })
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