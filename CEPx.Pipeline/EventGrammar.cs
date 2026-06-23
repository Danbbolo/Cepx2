using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    private const double SWEEP_THRESHOLD_PCT = 0.2;
    private const int SWEEP_WINDOW_TICKS = 5;
    private const double MIN_VOLUME_MULTIPLIER = 2.0;

    /// Pure detector: returns CepEvent for EVERY sweep meeting basic price threshold.
    /// No volume filter, no direction filter — L4 (PolicyEngine) decides entry.
    public static CepEvent? DetectSweepStart(MarketEvent[] window)
    {
        if (window.Length < SWEEP_WINDOW_TICKS) return null;
        var recent = new ArraySegment<MarketEvent>(window, window.Length - SWEEP_WINDOW_TICKS, SWEEP_WINDOW_TICKS);
        var prices = new double[SWEEP_WINDOW_TICKS];
        for (int i = 0; i < SWEEP_WINDOW_TICKS; i++) { prices[i] = recent[i].Price; }
        var high = prices.Max();
        var low = prices.Min();
        var avgPrice = prices.Average();
        if (avgPrice <= 0) return null;
        if ((high - low) / avgPrice * 100 < SWEEP_THRESHOLD_PCT) return null;
        var cur = recent[recent.Count - 1];
        string dir = prices[SWEEP_WINDOW_TICKS - 1] > prices[0] ? "bullish" : "bearish";
        return new CepEvent(cur.Timestamp, cur.Symbol, "SweepStart", cur.Price, dir);
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

        // ── Score: volume intensity (60%) + price stability (40%) ──
        double volRatio = last.Volume / avg; // 3x min, 8x+ = full
        double volScore = Clamp01((volRatio - 3.0) / 5.0);  // 3x→0.0, 5.5x→0.5, 8x→1.0
        double stabilityScore = Clamp01(1.0 - (pct / 0.1)); // 0.0%→1.0, 0.1%→0.0
        double score = volScore * 0.60 + stabilityScore * 0.40;

        string context = $"score:{score:F2}";
        return new CepEvent(last.Timestamp, last.Symbol, "AbsorptionAfterSweep", last.Price, context);
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

    /// <summary>
    /// Detects exhaustion: a directional impulse followed by deceleration or reversal.
    /// Scans all adjacent tick-pairs in the window for the strongest exhaustion pattern.
    /// </summary>
    public static CepEvent? DetectExhaustionPulse(MarketEvent[] window)
    {
        if (window.Length < 4) return null;
        int n = window.Length;

        // Scan all consecutive tick pairs for exhaustion pattern:
        // firstHalf = price change over first N ticks, secondHalf = change over next M ticks
        // Exhaustion = firstHalf is directional, secondHalf is flat or reversing
        double bestScore = 0;
        int bestIdx = 0;
        bool bestReversed = false;
        double bestFirstMovePct = 0, bestRevRatio = 0;

        for (int start = 0; start <= n - 4; start++)
        {
            int mid = start + 2;
            double pStart = window[start].Price;
            double pMid = window[mid].Price;
            double pEnd = window[Math.Min(mid + 2, n - 1)].Price;

            double firstMove = pMid - pStart;
            double secondMove = pEnd - pMid;
            double firstMovePct = Math.Abs(firstMove) / pStart * 100;

            if (firstMovePct < 0.15) continue;

            bool reversed = firstMove * secondMove <= 0;
            double revRatio = Math.Abs(secondMove) / Math.Max(Math.Abs(firstMove), 1e-10);

            // Must either reverse direction or decelerate to < 25%
            if (!reversed && revRatio >= 0.25) continue;
            if (reversed && revRatio < 0.2) continue;

            double magScore = Clamp01(firstMovePct / 0.4);
            double revScore;
            if (reversed)
                revScore = Clamp01((revRatio - 0.2) / 0.8);
            else
                revScore = Clamp01((0.25 - revRatio) / 0.25);

            double score = magScore * 0.40 + revScore * 0.60;
            if (score > bestScore) { bestScore = score; bestIdx = mid; bestReversed = reversed; bestFirstMovePct = firstMovePct; bestRevRatio = revRatio; }
        }

        if (bestScore <= 0 || bestScore < 0.5) return null; // minimum score gate

        var last = window[n - 1];
        var ctx = bestReversed ? "reversal_exhaustion" : "deceleration_exhaustion";
        string context = $"{ctx}:score:{bestScore:F2}";
        return new CepEvent(last.Timestamp, last.Symbol, "ExhaustionPulse", last.Price, context);
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
