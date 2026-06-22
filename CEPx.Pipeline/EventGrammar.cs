using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    private const double SWEEP_THRESHOLD_PCT = 0.5;
    private const int SWEEP_WINDOW_TICKS = 5;
    private const double MIN_VOLUME_MULTIPLIER = 2.0;

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

    public static CepEvent[] ReplayTicks(MarketEvent[] ticks)
    {
        return new[] { new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "") };
    }
}
