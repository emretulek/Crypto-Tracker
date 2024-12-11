using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Web;
using System.Windows;
using System.Windows.Media.Imaging;
using Widgets.Common;

namespace CryptoTracker
{
    internal class Request:IDisposable
    {
        private static readonly string _exchanheInfoUrl = "https://api.binance.com/api/v3/exchangeInfo?symbolStatus=TRADING";
        private static readonly string _tradingDayUrl = "https://api.binance.com/api/v3/ticker/tradingDay";
        private static readonly string _imageUrl = "https://bin.bnbstatic.com/static/assets/logos/";
        private static readonly string _streamTradeWss = "wss://stream.binance.com:443/stream";

        private static WebSocket4Net.WebSocket? ClientSocket;
        private readonly HttpClient HttpClient = new();
        private bool ClientManuelClosed = false;
        private readonly Dictionary<string, float> OpenPrice = [];
        private int OpenPriceAttemption = 0;
        private int OpenPriceAttemptionMax = 10;
        private readonly CancellationTokenSource UIUpdaterToken = new();
        private readonly int ReConnectTime = 30000;
        private readonly int ReConnectMaxAttempt = 10;
        private int ReConnectAttemptCount = 0;

        /// <summary>
        /// Load Crypto Assets
        /// </summary>
        /// <returns></returns>
        public async Task LoadCoinPairsAsync(List<CoinPair> coinPairs)
        {
            try
            {
                var response = await HttpClient.GetStringAsync(_exchanheInfoUrl);
                var data = JObject.Parse(response);
                var symbols = data["symbols"] ?? (JObject)"";

                foreach (var symbol in symbols)
                {
                    var _symbol = symbol["symbol"]?.ToString();
                    var _aseAsset = symbol["baseAsset"]?.ToString();
                    var _quoteAsset = symbol["quoteAsset"]?.ToString();

                    if (_symbol != null && _aseAsset != null && _quoteAsset != null)
                    {
                        coinPairs.Add(new CoinPair
                        {
                            Symbol = _symbol,
                            BaseAsset = _aseAsset,
                            QuoteAsset = _quoteAsset
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }

        /// <summary>
        /// Load Crypto Assets
        /// </summary>
        /// <returns></returns>
        public async Task GetOpenPrice(CoinPair coinPairSelected)
        {
            ObservableCollection<CoinPair> coinPairs = [coinPairSelected];
            await GetOpenPrices(coinPairs);
        }

        /// <summary>
        /// Load Crypto Assets
        /// </summary>
        /// <returns></returns>
        public async Task GetOpenPrices(ObservableCollection<CoinPair> coinPairGridRows)
        {
            if (!coinPairGridRows.Any()) return;

            ObservableCollection<CoinPair> coinPairsOpenPriceFailed = [];
            List<string> uniqueCoinPairs = [];
            string parameters = "[";

            foreach (CoinPair coinPair in coinPairGridRows)
            {
                if (!uniqueCoinPairs.Contains(coinPair.Symbol))
                {
                    uniqueCoinPairs.Add(coinPair.Symbol);
                    parameters += $"\"{coinPair.Symbol}\",";
                }
            }

            parameters = parameters.TrimEnd(',') + "]";

            try
            {
                var response = await HttpClient.GetStringAsync($"{_tradingDayUrl}?symbols={HttpUtility.UrlEncode(parameters)}");
                var data = JArray.Parse(response);

                if (data == null)
                {
                    throw new Exception("Can not fetch data.");
                }

                foreach (var item in data)
                {
                    var coinPair = coinPairGridRows.FirstOrDefault(pair => pair.Symbol == item["symbol"]?.ToString());

                    if (coinPair == null)
                    {
                        continue;
                    }

                    var openPrice = float.TryParse(item["openPrice"]?.ToString(), out var _openPrice) ? _openPrice : 0;
                    OpenPrice[coinPair.Symbol] = openPrice;

                    if(openPrice == 0)
                    {
                        coinPairsOpenPriceFailed.Add(coinPair);
                        Logger.Warning($"Open price is 0 Symbol: {coinPair.Symbol}");
                    }
                }

                // if open price is 0
                if (OpenPriceAttemption <= OpenPriceAttemptionMax)
                {
                    if (coinPairsOpenPriceFailed.Count > 0)
                    {
                        OpenPriceAttemption++;
                        await Task.Delay(10000).ContinueWith(t => GetOpenPrices(coinPairsOpenPriceFailed));
                    }
                    else
                    {
                        OpenPriceAttemption = 0;
                    }
                }

                Logger.Info($"Open price updated");
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinPairGridRow"></param>
        /// <returns></returns>
        public async Task GetCoinImage(CoinPair coinPairGridRow)
        {
            try
            {
                HttpResponseMessage response = await HttpClient.GetAsync($"{_imageUrl}{coinPairGridRow.BaseAsset}.png");
                response.EnsureSuccessStatusCode();

                using Stream stream = await response.Content.ReadAsStreamAsync();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                coinPairGridRow.Image = bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="coinPairGridRows"></param>
        /// <returns></returns>
        public async Task WebSocketConnection(ObservableCollection<CoinPair> coinPairGridRows)
        {
            var dataQueue = new ConcurrentQueue<JObject>();

            ClientSocket = new(_streamTradeWss);
            ClientSocket.Opened += (sender, e) =>
            {
                Logger.Info("WebSocket connected.");

                // Socket açılmadan pencere kapatılmışsa socketi kapat
                if (MainWindow.WindowClosed)
                {
                    ClientSocket.Close();
                    return;
                }

                ClientManuelClosed = false;
                Subscribes(coinPairGridRows);
                _ = UIUpdater(dataQueue, coinPairGridRows);
            };

            ClientSocket.MessageReceived += (sender, e) =>
            {
                JObject data = JObject.Parse(e.Message);
                JObject? item = (JObject?)(data["data"]);

                if (item != null)
                {
                    if (dataQueue.Count > 100)
                    {
                        dataQueue.TryDequeue(out var deleted);
                    }
                    dataQueue.Enqueue(item);
                }
            };

            ClientSocket.Closed += async (sender, e) =>
            {
                try
                {
                    Logger.Info("WebSocket closed.");

                    if (!ClientManuelClosed)
                    {
                        await WebSocketReConnect(coinPairGridRows); 
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            };

            ClientSocket.Error += (sender, e) =>
            {
                Logger.Error($"WebSocket error: {e.Exception.Message}");
            };
 
            if(ClientSocket.State == WebSocket4Net.WebSocketState.None ||
                ClientSocket.State == WebSocket4Net.WebSocketState.Closed)
            {
                await ClientSocket.OpenAsync();
            }
        }

        public async Task WebSocketReConnect(ObservableCollection<CoinPair> coinPairGridRows)
        {
            if(ClientSocket?.State == WebSocket4Net.WebSocketState.Closed)
            {
                if (ReConnectAttemptCount > ReConnectMaxAttempt) return;
                ReConnectAttemptCount++;
                await Task.Delay(ReConnectTime);
                await WebSocketConnection(coinPairGridRows);
            }
        }

        // Subscribe one pair
        public void Subscribe(CoinPair coinPair)
        {
            ObservableCollection<CoinPair> coinPairs = [coinPair];
            Subscribes(coinPairs);
        }

        // Subscribe multiple pairs
        public void Subscribes(ObservableCollection<CoinPair> coinPairGridRows)
        {
            if (!coinPairGridRows.Any()) return;

            List<string> parameters = [];
            foreach (var coinPair in coinPairGridRows)
            {
                parameters.Add(coinPair.Symbol.ToLower() + "@miniTicker");
            }

            var request = new
            {
                method = "SUBSCRIBE",
                @params = parameters.ToArray(),
                id = 1
            };

            try
            {
                string message = Newtonsoft.Json.JsonConvert.SerializeObject(request);
                ClientSocket?.Send(message);

                Logger.Info($"Subscribed to {string.Join(",", parameters)}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }
            
        }

        // UnSubscribe one pair
        public void UnSubscribe(CoinPair coinPair)
        {
            ObservableCollection<CoinPair> coinPairs = [coinPair];
            UnSubscribes(coinPairs);
        }

        // UnSubscribe multiple pairs
        public void UnSubscribes(ObservableCollection<CoinPair> coinPairGridRows)
        {
            if (!coinPairGridRows.Any()) return;


                List<string> parameters = [];
                foreach (var coinPair in coinPairGridRows)
                {
                    parameters.Add(coinPair.Symbol.ToLower() + "@miniTicker");
                }

                var request = new
                {
                    method = "UNSUBSCRIBE",
                    @params = parameters.ToArray(),
                    id = 1
                };

                try
                {
                    string message = Newtonsoft.Json.JsonConvert.SerializeObject(request);
                    ClientSocket?.Send(message);

                    Logger.Info($"UnSubscribed to {string.Join(",", parameters)}");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message);
                }
            
        }

        // Close Websocket connection
        public async Task CloseWebSockets()
        {
            try
            {
                if (ClientSocket?.State == WebSocket4Net.WebSocketState.Open)
                {
                    ClientManuelClosed = true;
                    await ClientSocket.CloseAsync();
                }

            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
            }  
        }

        // UI updater with time limit
        public async Task UIUpdater(ConcurrentQueue<JObject> dataQueue, ObservableCollection<CoinPair> coinPairGridRows)
        {
            await Task.Run(async () =>
            {
                try
                {
                    while (!UIUpdaterToken.IsCancellationRequested)
                    {
                        if (dataQueue.TryDequeue(out var data))
                        {
                            var symbol = data["s"]?.ToString();
                            var coinPairGridRow = coinPairGridRows.FirstOrDefault(p => p.Symbol == symbol);

                            if (coinPairGridRow != null && OpenPrice.TryGetValue(coinPairGridRow.Symbol, out float open_price))
                            {
                                double last_price = double.Parse(data["c"]?.ToString() ?? "0");
                                double change = last_price - open_price;
                                double change_percent = (change / open_price) * 100;

                                var sameCoinPairGridRows = coinPairGridRows.Where(p => p.Symbol == symbol);

                                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    foreach (var coinPair in sameCoinPairGridRows)
                                    {
                                        coinPair.Price = last_price;
                                        coinPair.Change = change;
                                        coinPair.ChangePercent = change_percent;
                                    }
                                });
                            }
                        }
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.Message);
                }
            }, UIUpdaterToken.Token);
        }

        public void Dispose() {
            HttpClient.Dispose();
            ClientSocket?.Dispose();
            UIUpdaterToken.Cancel();
            UIUpdaterToken.Dispose();
            GC.SuppressFinalize(this);
            Logger.Info("WebSocket disposed.");
        }
    }
}
