// TODO: Migrate to Rx.NET observables when event volume exceeds 1000/sec

using CEPx.Core;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    /// Stream live ticks to pipeline.
    public static IDisposable ConnectBinanceFeed(string symbol, Action<MarketEvent> onTick)
    {
        if (!LiveMode)
        {
            var cts = new CancellationTokenSource();
            var sym = symbol;
            _ = Task.Run(async () =>
            {
                foreach (var tick in SyntheticTicks(sym))
                {
                    if (cts.IsCancellationRequested) break;
                    onTick(tick);
                    await Task.Delay(100);
                }
            });
            return cts;
        }

        var cancel = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    var uri = $"wss://fstream.binance.com/ws/{symbol.ToLower()}@aggTrade";
                    await ws.ConnectAsync(new Uri(uri), cancel.Token);
                    var buffer = new byte[4096];
                    while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buffer, cancel.Token);
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var tick = ParseAggTrade(json, symbol);
                        if (tick.HasValue) onTick(tick.Value);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WS: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        });
        return cancel;
    }

    private static MarketEvent? ParseAggTrade(string json, string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var price = double.Parse(r.GetProperty("p").GetString()!);
            var qty = double.Parse(r.GetProperty("q").GetString()!);
            var ts = r.GetProperty("T").GetInt64();
            return new MarketEvent(ts, symbol, price, qty, 0, 0, 0);
        }
        catch { return null; }
    }

    public static MarketEvent[] SyntheticTicks(string symbol)
    {
        var now = 0L;
        return new MarketEvent[]
        {
            // ── A: Gradual uptrend + sweep (0-19) ──
            new(now,       symbol, 42000.0, 1.0, 0, 0, 0),
            new(now+100,   symbol, 42020.0, 1.2, 0, 0, 0),
            new(now+200,   symbol, 42040.0, 1.1, 0, 0, 0),
            new(now+300,   symbol, 42060.0, 1.3, 0, 0, 0),
            new(now+400,   symbol, 42080.0, 1.5, 0, 0, 0),
            new(now+500,   symbol, 42100.0, 1.4, 0, 0, 0),
            new(now+600,   symbol, 42120.0, 1.6, 0, 0, 0),
            new(now+700,   symbol, 42140.0, 1.3, 0, 0, 0),
            new(now+800,   symbol, 42160.0, 1.8, 0, 0, 0),
            new(now+900,   symbol, 42180.0, 1.5, 0, 0, 0),
            new(now+1000,  symbol, 42200.0, 2.0, 0, 0, 0),
            new(now+1100,  symbol, 42220.0, 1.7, 0, 0, 0),
            new(now+1200,  symbol, 42240.0, 1.9, 0, 0, 0),
            new(now+1300,  symbol, 42260.0, 2.1, 0, 0, 0),
            new(now+1400,  symbol, 42280.0, 2.3, 0, 0, 0),
            new(now+1500,  symbol, 42400.0, 8.0, 0, 0, 0), // sweep fires here
            new(now+1600,  symbol, 42450.0, 4.0, 0, 0, 0),
            new(now+1700,  symbol, 42480.0, 3.0, 0, 0, 0),
            new(now+1800,  symbol, 42500.0, 2.5, 0, 0, 0),
            new(now+1900,  symbol, 42520.0, 2.0, 0, 0, 0),

            // ── B: Range-bound (20-39) ──
            new(now+2000,  symbol, 42500.0, 1.5, 0, 0, 0),
            new(now+2100,  symbol, 42530.0, 1.6, 0, 0, 0),
            new(now+2200,  symbol, 42510.0, 1.4, 0, 0, 0),
            new(now+2300,  symbol, 42540.0, 1.5, 0, 0, 0),
            new(now+2400,  symbol, 42520.0, 1.3, 0, 0, 0),
            new(now+2500,  symbol, 42550.0, 1.6, 0, 0, 0),
            new(now+2600,  symbol, 42530.0, 1.5, 0, 0, 0),
            new(now+2700,  symbol, 42560.0, 1.4, 0, 0, 0),
            new(now+2800,  symbol, 42540.0, 1.5, 0, 0, 0),
            new(now+2900,  symbol, 42570.0, 1.3, 0, 0, 0),
            new(now+3000,  symbol, 42550.0, 1.6, 0, 0, 0),
            new(now+3100,  symbol, 42580.0, 1.4, 0, 0, 0),
            new(now+3200,  symbol, 42560.0, 1.5, 0, 0, 0),
            new(now+3300,  symbol, 42590.0, 1.3, 0, 0, 0),
            new(now+3400,  symbol, 42570.0, 1.5, 0, 0, 0),
            new(now+3500,  symbol, 42600.0, 1.4, 0, 0, 0),
            new(now+3600,  symbol, 42580.0, 1.6, 0, 0, 0),
            new(now+3700,  symbol, 42610.0, 1.5, 0, 0, 0),
            new(now+3800,  symbol, 42590.0, 1.4, 0, 0, 0),
            new(now+3900,  symbol, 42620.0, 1.3, 0, 0, 0),

            // ── C: Breakout attempt (40-59) ──
            new(now+4000,  symbol, 42600.0, 1.2, 0, 0, 0),
            new(now+4100,  symbol, 42610.0, 1.3, 0, 0, 0),
            new(now+4200,  symbol, 42605.0, 1.1, 0, 0, 0),
            new(now+4300,  symbol, 42620.0, 1.4, 0, 0, 0),
            new(now+4400,  symbol, 42615.0, 1.2, 0, 0, 0),
            new(now+4500,  symbol, 42630.0, 1.3, 0, 0, 0),
            new(now+4600,  symbol, 42625.0, 1.1, 0, 0, 0),
            new(now+4700,  symbol, 42640.0, 1.5, 0, 0, 0),
            new(now+4800,  symbol, 42635.0, 1.2, 0, 0, 0),
            new(now+4900,  symbol, 42650.0, 1.4, 0, 0, 0),
            new(now+5000,  symbol, 42900.0, 5.0, 0, 0, 0), // breakout above range
            new(now+5100,  symbol, 42950.0, 3.5, 0, 0, 0),
            new(now+5200,  symbol, 43000.0, 3.0, 0, 0, 0),
            new(now+5300,  symbol, 43050.0, 2.5, 0, 0, 0),
            new(now+5400,  symbol, 43100.0, 2.0, 0, 0, 0),
            new(now+5500,  symbol, 43150.0, 1.8, 0, 0, 0),
            new(now+5600,  symbol, 43200.0, 1.5, 0, 0, 0),
            new(now+5700,  symbol, 43250.0, 1.3, 0, 0, 0),
            new(now+5800,  symbol, 43300.0, 1.2, 0, 0, 0),
            new(now+5900,  symbol, 43250.0, 1.8, 0, 0, 0),

            // ── D: Exhaustion (60-79) ──
            new(now+6000,  symbol, 43250.0, 1.5, 0, 0, 0),
            new(now+6100,  symbol, 43300.0, 1.6, 0, 0, 0),
            new(now+6200,  symbol, 43400.0, 1.8, 0, 0, 0),
            new(now+6300,  symbol, 43500.0, 2.0, 0, 0, 0),
            new(now+6400,  symbol, 43600.0, 2.5, 0, 0, 0),
            new(now+6500,  symbol, 43700.0, 3.0, 0, 0, 0),
            new(now+6600,  symbol, 43750.0, 3.5, 0, 0, 0),
            new(now+6700,  symbol, 43800.0, 4.0, 0, 0, 0),
            new(now+6800,  symbol, 43850.0, 5.0, 0, 0, 0),
            new(now+6900,  symbol, 43900.0, 6.0, 0, 0, 0), // peak
            new(now+7000,  symbol, 43700.0, 4.0, 0, 0, 0), // exhaustion reversal
            new(now+7100,  symbol, 43500.0, 3.5, 0, 0, 0),
            new(now+7200,  symbol, 43300.0, 3.0, 0, 0, 0),
            new(now+7300,  symbol, 43200.0, 2.5, 0, 0, 0),
            new(now+7400,  symbol, 43150.0, 2.0, 0, 0, 0),
            new(now+7500,  symbol, 43100.0, 1.8, 0, 0, 0),
            new(now+7600,  symbol, 43050.0, 1.5, 0, 0, 0),
            new(now+7700,  symbol, 43000.0, 1.3, 0, 0, 0),
            new(now+7800,  symbol, 42950.0, 1.2, 0, 0, 0),
            new(now+7900,  symbol, 42900.0, 1.0, 0, 0, 0),

            // ── E: Reclaim after sweep (80-99) ──
            new(now+8000,  symbol, 43000.0, 1.5, 0, 0, 0),
            new(now+8100,  symbol, 42950.0, 2.0, 0, 0, 0),
            new(now+8200,  symbol, 42900.0, 2.5, 0, 0, 0),
            new(now+8300,  symbol, 42850.0, 3.0, 0, 0, 0),
            new(now+8400,  symbol, 42800.0, 5.0, 0, 0, 0), // sweep down
            new(now+8500,  symbol, 42900.0, 3.0, 0, 0, 0), // reclaim above origin
            new(now+8600,  symbol, 42950.0, 2.5, 0, 0, 0),
            new(now+8700,  symbol, 43000.0, 2.0, 0, 0, 0),
            new(now+8800,  symbol, 43050.0, 1.8, 0, 0, 0),
            new(now+8900,  symbol, 43100.0, 1.5, 0, 0, 0),
            new(now+9000,  symbol, 43150.0, 1.3, 0, 0, 0),
            new(now+9100,  symbol, 43200.0, 1.2, 0, 0, 0),
            new(now+9200,  symbol, 43250.0, 1.0, 0, 0, 0),
            new(now+9300,  symbol, 43300.0, 1.1, 0, 0, 0),
            new(now+9400,  symbol, 43350.0, 1.2, 0, 0, 0),
            new(now+9500,  symbol, 43400.0, 1.3, 0, 0, 0),
            new(now+9600,  symbol, 43450.0, 1.4, 0, 0, 0),
            new(now+9700,  symbol, 43500.0, 1.5, 0, 0, 0),
            new(now+9800,  symbol, 43550.0, 1.6, 0, 0, 0),
            new(now+9900,  symbol, 43600.0, 1.8, 0, 0, 0),
        };
    }

    public static MarketEvent[] FetchBinanceHistorical(string symbol, string interval = "1m", int limit = 100)
    {
        using var http = new HttpClient();
        var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var json = http.GetStringAsync(url).Result;
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.EnumerateArray();
        var result = new List<MarketEvent>();
        int seq = 0;
        foreach (var row in rows)
        {
            var arr = row.EnumerateArray().ToArray();
            long ts = arr[0].GetInt64();
            double close = double.Parse(arr[4].GetString()!);
            double vol = double.Parse(arr[5].GetString()!);
            result.Add(new MarketEvent(ts, symbol, close, vol, 0, 0, seq++));
        }
        return result.ToArray();
    }
}
