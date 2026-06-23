using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects price consolidation: a narrowing range that precedes breakout moves.
/// When the price compresses into a tight range and volume is thin, the market
/// is coiling — a breakout is likely imminent.
///
/// Scoring (0.0–1.0):
///   A. Range tightness — how tight is the range vs recent swings? (0.3× normal → 1.0)
///   B. Time in consolidation — how long has it been compressing? (≥10 ticks → 1.0)
///   C. Volume thinness — is volume drying up during the compression? (thin → 1.0)
///
/// Output: CepEvent with Type "Consolidation", score in Context.
/// </summary>
public class ConsolidationDetector
{
    /// <summary>
    /// Detect consolidation using current window and swing tracker state.
    /// </summary>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        double currentSwingRange,
        double recentAvgVolume,
        double dailyAvgVolume,
        int ticksInRange = 0,
        EventGrammarConfig? config = null)
    {
        var cfg = config ?? new EventGrammarConfig();
        int n = priceWindow.Length;
        if (n < 5) return null;

        // ── Compute window range ─────────────────────────────────
        double windowHigh = double.MinValue, windowLow = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (priceWindow[i].Price > windowHigh) windowHigh = priceWindow[i].Price;
            if (priceWindow[i].Price < windowLow) windowLow = priceWindow[i].Price;
        }
        double windowRange = (windowHigh - windowLow) / windowLow * 100;
        double midPrice = (windowHigh + windowLow) / 2;

        // ── Compare to swing range ───────────────────────────────
        double swingRangePct = currentSwingRange > 0
            ? currentSwingRange / midPrice * 100 : 0;

        // If no swing context, use window itself
        if (swingRangePct <= 0) swingRangePct = windowRange;

        double tightnessRatio = swingRangePct > 0
            ? windowRange / swingRangePct : 1.0;

        // ── Gate: must be compressing ────────────────────────────
        if (tightnessRatio > 0.4) return null; // range > 40% of swing range = not consolidating

        // ── A. Range tightness ────────────────────────────────────
        // 0.4 of swing → 0.0, 0.1 of swing → 1.0
        double tightnessScore = Clamp01(1.0 - (tightnessRatio - 0.1) / 0.3);

        // ── B. Volume thinness during consolidation ───────────────
        double windowAvgVol = 0;
        for (int i = 0; i < n; i++) windowAvgVol += priceWindow[i].Volume;
        windowAvgVol /= n;

        double volRatio = dailyAvgVolume > 0 ? windowAvgVol / dailyAvgVolume : 1.0;
        // Below 50% of daily → 1.0, at 80% → 0.0
        double thinScore = Clamp01(1.0 - (volRatio - 0.5) / 0.3);

        // ── C. Time in consolidation (optional) ───────────────────
        double timeScore = Clamp01(ticksInRange / 10.0);

        // ── Combined ──────────────────────────────────────────────
        double score = Clamp01(tightnessScore * 0.50 + thinScore * 0.30 + timeScore * 0.20);

        if (score < 0.3) return null; // too weak

        var last = priceWindow[n - 1];
        string context = $"score:{score:F2}:tight={tightnessRatio:F2}:volRatio={volRatio:F2}";
        return new CepEvent(last.Timestamp, last.Symbol, "Consolidation", last.Price, context);
    }

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1.0, v));
}
