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
        return new[]
        {
            new MarketEvent(now,       symbol, 42000.0, 1.0, 0, 0, 0),
            new MarketEvent(now+100,   symbol, 42030.0, 1.2, 0, 0, 0),
            new MarketEvent(now+200,   symbol, 42080.0, 1.5, 0, 0, 0),
            new MarketEvent(now+300,   symbol, 42150.0, 2.1, 0, 0, 0),
            new MarketEvent(now+400,   symbol, 42300.0, 5.0, 0, 0, 0),
        };
    }
}
