using CEPx.Core;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CEPx.Pipeline;

public static class PipelineFunctions
{
    private const double SWEEP_THRESHOLD_PCT = 0.5;
    private const int SWEEP_WINDOW_TICKS = 5;
    private const double MIN_VOLUME_MULTIPLIER = 2.0;

    public static bool LiveMode;

    // ── Phase 0 stubs ──────────────────────────────────────────────────

    public static CepEvent? IngestTick(MarketEvent tick)
    {
        return new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "");
    }

    public static StructuralScore ScoreEvent(CepEvent evt, MarketEvent[] window)
    {
        return new StructuralScore(0L, "", 0.0, 0.0, 0.0, 0.0, "", 0.0, 0.0);
    }

    public static BlackboardState WriteState(StructuralScore score)
    {
        return new BlackboardState(0L, "", false, "", 0.0, 0.0, 0.0, 0.0, 0.0, "", 0.0, "");
    }

    public static PolicyDecision Decide(BlackboardState state)
    {
        return new PolicyDecision(0L, "BTCUSDT", "noop", "", "", 0.0);
    }

    public static CepEvent[] ReplayTicks(MarketEvent[] ticks)
    {
        return new[] { new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "") };
    }

    // ── Phase 1: Ingestion + SweepStart ────────────────────────────────

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

    /// Find price-volume sweep in window.
    public static CepEvent? DetectSweepStart(MarketEvent[] window)
    {
        if (window.Length < SWEEP_WINDOW_TICKS) return null;
        var recent = new ArraySegment<MarketEvent>(window, window.Length - SWEEP_WINDOW_TICKS, SWEEP_WINDOW_TICKS);
        var prices = new double[SWEEP_WINDOW_TICKS];
        var volumes = new double[SWEEP_WINDOW_TICKS];
        for (int i = 0; i < SWEEP_WINDOW_TICKS; i++) { prices[i] = recent[i].Price; volumes[i] = recent[i].Volume; }
        var high = prices.Max();
        var low = prices.Min();
        var avgPrice = prices.Average();
        if (avgPrice <= 0) return null;
        if ((high - low) / avgPrice * 100 < SWEEP_THRESHOLD_PCT) return null;
        var avgVol = volumes.Average();
        if (avgVol <= 0) return null;
        var cur = recent[^1];
        if (cur.Volume <= avgVol * MIN_VOLUME_MULTIPLIER) return null;
        return new CepEvent(cur.Timestamp, cur.Symbol, "SweepStart", cur.Price, "");
    }

    /// Full replay for testing validation.
    public static CepEvent[] RunPipeline(MarketEvent[] ticks)
    {
        var events = new List<CepEvent>();
        var buf = new MarketEvent[SWEEP_WINDOW_TICKS];
        for (int i = 0; i < ticks.Length; i++)
        {
            buf[i % SWEEP_WINDOW_TICKS] = ticks[i];
            if (i < SWEEP_WINDOW_TICKS - 1) continue;
            var window = new MarketEvent[SWEEP_WINDOW_TICKS];
            for (int j = 0; j < SWEEP_WINDOW_TICKS; j++) window[j] = buf[(i - SWEEP_WINDOW_TICKS + 1 + j) % SWEEP_WINDOW_TICKS];
            var hit = DetectSweepStart(window);
            if (hit.HasValue) events.Add(hit.Value);
        }
        return events.ToArray();
    }

    // ── helpers ────────────────────────────────────────────────────────

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

    private static MarketEvent[] SyntheticTicks(string symbol)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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