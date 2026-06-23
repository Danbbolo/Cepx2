using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects when a directional move continues WITHOUT meaningful opposing absorption.
/// Absorption = high volume + minimal price movement. Its ABSENCE during a directional
/// move suggests the move is clean and likely to continue.
/// 
/// This is the inverse of AbsorptionAfterSweep: instead of looking for a spike bar
/// that stalls price, we verify that price keeps moving and NO spike bar appeared.
/// 
/// Scoring (0.0–1.0) — three dimensions, configurable via EventGrammarConfig:
///   A. Price continuation — how far did price move relative to the window?
///   B. Volume containment — how far below the absorption threshold is the max volume?
///   C. Movement smoothness — is the move clean (no whipsaw stalls)?
/// 
/// Output: CepEvent with Type "NoMeaningfulAbsorption", score in Context.
/// </summary>
public class NoMeaningfulAbsorptionDetector
{
    /// <summary>
    /// Detect absence of meaningful absorption in a price window.
    /// </summary>
    /// <param name="priceWindow">Recent price ticks (should match NoAbsorptionWindowTicks in length).</param>
    /// <param name="config">Optional config override.</param>
    /// <returns>CepEvent with score, or null if absorption-like behavior is detected.</returns>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        EventGrammarConfig? config = null)
    {
        var cfg = config ?? new EventGrammarConfig();
        int n = priceWindow.Length;

        if (n < 2) return null;

        double firstPrice = priceWindow[0].Price;
        double lastPrice = priceWindow[n - 1].Price;

        // ── Pass 1: collect stats ────────────────────────────────
        double totalVolume = 0;
        double maxVolume = 0;
        double maxVolumePriceMove = 0; // price move during the max-volume tick
        int maxVolumeIndex = 0;
        double minPrice = double.MaxValue, maxPrice = double.MinValue;

        for (int i = 0; i < n; i++)
        {
            var tick = priceWindow[i];
            totalVolume += tick.Volume;
            if (tick.Price < minPrice) minPrice = tick.Price;
            if (tick.Price > maxPrice) maxPrice = tick.Price;

            if (tick.Volume > maxVolume)
            {
                maxVolume = tick.Volume;
                maxVolumeIndex = i;
            }
        }

        double avgVolume = totalVolume / n;

        // Price move during the max-volume tick (vs previous tick)
        if (maxVolumeIndex > 0)
        {
            maxVolumePriceMove = Math.Abs(
                (priceWindow[maxVolumeIndex].Price - priceWindow[maxVolumeIndex - 1].Price)
                / priceWindow[maxVolumeIndex - 1].Price * 100.0);
        }

        // ── Gate: minimum directional movement ──
        double netMovePct = Math.Abs((lastPrice - firstPrice) / firstPrice * 100.0);
        if (netMovePct < cfg.NoAbsorptionMinNetMovePct) return null;

        // Determine direction
        bool isUp = lastPrice > firstPrice;

        // ── Gate: check for absorption-like behavior ─────────────
        double volRatio = maxVolume / Math.Max(avgVolume, 1e-10);
        bool hasAbsorptionCandidate = volRatio >= cfg.NoAbsorptionMaxVolumeMultiplier
                                      && maxVolumePriceMove < 0.05;

        // If absorption IS present, this detector returns null (no signal)
        if (hasAbsorptionCandidate) return null;

        // ── Scoring ──────────────────────────────────────────────

        // A. Price continuation: how much did price move?
        double moveNorm = cfg.NoAbsorptionMoveNormPct;
        double continuationScore = Clamp01(netMovePct / moveNorm);

        // B. Volume containment: how far below the absorption threshold?
        // volRatio at 1.0 (avg) → 1.0, at threshold → 0.0
        double threshold = cfg.NoAbsorptionMaxVolumeMultiplier;
        double containmentScore = Clamp01(1.0 - (volRatio - 1.0) / (threshold - 1.0));

        // C. Movement smoothness: does price move in one direction without whipsaw?
        double smoothnessScore = ScoreMovementSmoothness(priceWindow, isUp);

        // ── Combined score ───────────────────────────────────────
        // Weights: continuation 50%, containment 30%, smoothness 20%
        double score = Clamp01(
            continuationScore * 0.50 + containmentScore * 0.30 + smoothnessScore * 0.20);

        // ── Minimum score gate: discard weak signals ─────────────
        if (score < cfg.NoAbsorptionMinScore) return null;

        string dir = isUp ? "up" : "down";
        string context = $"score:{score:F2}:dir={dir}:volRatio={volRatio:F1}:move={netMovePct:F2}%";

        return new CepEvent(priceWindow[n - 1].Timestamp, priceWindow[n - 1].Symbol,
            "NoMeaningfulAbsorption", lastPrice, context);
    }

    // ── Scoring helpers ──────────────────────────────────────────────

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>
    /// Score movement smoothness: what fraction of ticks move in the dominant direction?
    /// Returns 0.3 (choppy/whippy) to 1.0 (every tick in same direction).
    /// </summary>
    private static double ScoreMovementSmoothness(MarketEvent[] window, bool isUp)
    {
        int aligned = 0, total = 0;
        for (int i = 1; i < window.Length; i++)
        {
            total++;
            double ret = window[i].Price - window[i - 1].Price;
            bool tickUp = ret > 1e-8;
            bool tickDown = ret < -1e-8;

            if ((isUp && tickUp) || (!isUp && tickDown)) aligned++;
            // Flat ticks count as neutral (half credit)
            else if (!tickUp && !tickDown) aligned++;
        }

        if (total == 0) return 0.5;
        double ratio = (double)aligned / total;

        // Remap: 0.5→0.3, 0.75→0.6, 1.0→1.0
        return Clamp01(0.3 + (ratio - 0.5) / 0.5 * 0.7);
    }
}
