using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    private const double SWEEP_THRESHOLD_PCT = 0.2;
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
        var cur = recent[recent.Count - 1];
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

    public static CepEvent? DetectReclaim(MarketEvent[] window, double sweepOriginPrice, bool isBullishSweep)
    {
        var cur = window[^1];
        if (isBullishSweep && cur.Price < sweepOriginPrice)
            return new CepEvent(cur.Timestamp, cur.Symbol, "Reclaim", cur.Price, "");
        if (!isBullishSweep && cur.Price > sweepOriginPrice)
            return new CepEvent(cur.Timestamp, cur.Symbol, "Reclaim", cur.Price, "");
        return null;
    }

    public static CepEvent? DetectAbsorption(MarketEvent[] window)
    {
        if (window.Length < 5) return null;
        var last5 = new ArraySegment<MarketEvent>(window, window.Length - 5, 5);
        double avg = (last5[0].Volume + last5[1].Volume + last5[2].Volume + last5[3].Volume) / 4.0;
        var last = last5[4];
        if (last.Volume <= avg * 3.0) return null;
        var prev = last5[3];
        double pct = Math.Abs(last.Price - prev.Price) / prev.Price * 100;
        if (pct >= 0.1) return null;
        return new CepEvent(last.Timestamp, last.Symbol, "AbsorptionAfterSweep", last.Price, "");
    }

    public static CepEvent? DetectBreakoutAttempt(MarketEvent[] window)
    {
        if (window.Length < 11) return null;
        var range = new ArraySegment<MarketEvent>(window, window.Length - 11, 10);
        double rangeHigh = double.MinValue, rangeLow = double.MaxValue;
        for (int i = 0; i < 10; i++) { if (range[i].Price > rangeHigh) rangeHigh = range[i].Price; if (range[i].Price < rangeLow) rangeLow = range[i].Price; }
        var cur = window[^1];
        if (cur.Price > rangeHigh * 1.002)
            return new CepEvent(cur.Timestamp, cur.Symbol, "BreakoutAttempt", cur.Price, "bullish");
        if (cur.Price < rangeLow * 0.998)
            return new CepEvent(cur.Timestamp, cur.Symbol, "BreakoutAttempt", cur.Price, "bearish");
        return null;
    }

    public static CepEvent? DetectExhaustionPulse(MarketEvent[] window)
    {
        if (window.Length < 6) return null;
        int l = window.Length;
        double p0 = window[l - 6].Price;
        double p2 = window[l - 4].Price;
        double p3 = window[l - 3].Price;
        double p5 = window[l - 1].Price;
        double firstMove = p2 - p0;
        double secondMove = p5 - p3;
        double firstMovePct = Math.Abs(firstMove) / p0 * 100;
        if (firstMovePct < 0.3) return null;
        if (firstMove * secondMove > 0) return null;
        if (Math.Abs(secondMove) < Math.Abs(firstMove) * 0.5) return null;
        var ctx = firstMove > 0 ? "bullish_exhaustion" : "bearish_exhaustion";
        return new CepEvent(window[l - 1].Timestamp, window[l - 1].Symbol, "ExhaustionPulse", p5, ctx);
    }
}
