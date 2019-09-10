//using Atomex.Common;
//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Atomex.MarketData.Bitfinex
//{
//    public class BitfinexRestApi
//    {
//        private string BaseUrl { get; } = "https://api.bitfinex.com/v2/";

//        public async Task<List<Candle>> GetCandles(
//            string symbol,
//            string timeFrame,
//            DateTime from,
//            DateTime to,
//            CancellationToken cancellationToken = default(CancellationToken))
//        {
//            var fromInMs = from.ToUniversalTime().ToUnixTimeMs();
//            var toInMs = to.ToUniversalTime().ToUnixTimeMs();

//            var request = $"candles/trade:{timeFrame}:{symbol}/hist?start={fromInMs}&end={toInMs}";

//            var candles = await HttpHelper.GetAsync(
//                    baseUri: BaseUrl,
//                    requestUri: request,
//                    responseHandler: responseContent =>
//                    {
//                        //var tickers = JsonConvert.DeserializeObject<JArray>(responseContent);

//                        //foreach (var tickerToken in tickers)
//                        //{
//                        //    if (!(tickerToken is JArray ticker))
//                        //        continue;

//                        //    var symbol = ticker[0].Value<string>();

//                        //    var bid = ticker[1].Value<decimal>();
//                        //    var ask = ticker[3].Value<decimal>();

//                        //    Quotes[symbol] = new Quote()
//                        //    {
//                        //        Bid = bid,
//                        //        Ask = ask
//                        //    };
//                        //}

//                        return true;
//                    },
//                    cancellationToken: cancellationToken)
//                .ConfigureAwait(false);
//        }
//    }
//}