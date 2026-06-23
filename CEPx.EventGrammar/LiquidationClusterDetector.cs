using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects liquidation clusters against a sweep direction.
/// Long liquidations (SELL) against a bullish sweep = trapped longs being stopped out = reversal signal.
/// Short liquidations (BUY) against a bearish sweep = trapped shorts being stopped out = reversal signal.
/// 
/// Scoring (0.0–1.0) — five dimensions, configurable via EventGrammarConfig:
///   A. Count — more liquidations = stronger (norm: LiquidationCountNorm)
///   B. Total quantity — larger notional = stronger (norm: LiquidationQtyNorm)
///   C. Whale bonus — a single huge liquidation (> LiquidationWhaleQty) adds extra weight
///   D. Time concentration — liquidations packed in a tight sub-window score higher
///   E. Direction ratio — what % of ALL liqs in window are against sweep (cascade detection)
///   F. Proximity bonus — liquidations near the sweep extreme are more meaningful
/// </summary>
public class LiquidationClusterDetector
{
    /// <summary>
    /// Detect a liquidation cluster using the provided config.
    /// Returns null if thresholds aren't met.
    /// </summary>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        LiquidationEvent[] liquidations,
        double sweepOriginPrice,
        bool isBullishSweep,
        long windowStartMs,
        long windowEndMs,
        EventGrammarConfig? config = null)
    {
        var cfg = config ?? new EventGrammarConfig();

        // ── Pass 1: collect stats ────────────────────────────────
        int count = 0;
        int totalInWindow = 0;
        double totalQty = 0;
        double maxSingleQty = 0;
        var liqTimestamps = new List<long>();
        int nearSweepCount = 0;

        foreach (var liq in liquidations)
        {
            if (liq.Timestamp < windowStartMs || liq.Timestamp > windowEndMs)
                continue;

            totalInWindow++;
            bool isAgainstSweep = isBullishSweep
                ? liq.IsLongLiquidation
                : liq.IsShortLiquidation;

            if (!isAgainstSweep) continue;

            count++;
            totalQty += liq.Quantity;
            if (liq.Quantity > maxSingleQty) maxSingleQty = liq.Quantity;
            liqTimestamps.Add(liq.Timestamp);

            double distFromSweep = Math.Abs(liq.Price - sweepOriginPrice) / sweepOriginPrice;
            if (distFromSweep <= cfg.LiquidationProximityPct)
                nearSweepCount++;
        }

        // DEBUG_LIQ: Log every detection attempt when liquidations exist in window
        if (totalInWindow > 0)
        {
            string sweepDir = isBullishSweep ? "bullish" : "bearish";
            Console.WriteLine($"[DEBUG_LIQ] Detect window ({sweepDir} sweep @ {sweepOriginPrice:F2}): " +
                $"{totalInWindow} liqs in window, {count} against sweep, totalQty={totalQty:F1}, maxSingle={maxSingleQty:F1}, nearSweep={nearSweepCount}");
        }
        // END DEBUG_LIQ

        // ── Gate: hard thresholds ─────────────────────────────────
        if (count < cfg.LiquidationMinCount || totalQty < cfg.LiquidationMinTotalQty)
        {
            // DEBUG_LIQ: near-miss logging
            if (count > 0)
            {
                Console.WriteLine($"[DEBUG_LIQ]   Near-miss: {count} against sweep (need {cfg.LiquidationMinCount}), " +
                    $"totalQty={totalQty:F1} (need {cfg.LiquidationMinTotalQty})");
            }
            // END DEBUG_LIQ
            return null;
        }

        // ── Scoring ──────────────────────────────────────────────

        // A. Count score — normalized to config target
        double countScore = Clamp01(count / cfg.LiquidationCountNorm);

        // B. Quantity score — normalized to config target
        double qtyScore = Clamp01(totalQty / cfg.LiquidationQtyNorm);

        // C. Whale bonus — a single massive liquidation is more informative than many small ones
        double whaleBonus = 0.0;
        if (maxSingleQty >= cfg.LiquidationWhaleQty)
        {
            // Scale: at exactly whaleQty → +0.05, at 2× whaleQty → +0.15 (capped)
            double whaleRatio = Clamp01((maxSingleQty - cfg.LiquidationWhaleQty) / cfg.LiquidationWhaleQty);
            whaleBonus = 0.05 + whaleRatio * 0.10;
        }

        // D. Time concentration — how tightly packed are the liquidations?
        double timeScore = ScoreTimeConcentration(liqTimestamps, windowStartMs, windowEndMs);

        // E. Direction ratio — what fraction of ALL liqs are against the sweep?
        double dirRatio = totalInWindow > 0 ? (double)count / totalInWindow : 0;
        double dirScore = Clamp01(dirRatio / cfg.LiquidationDirRatioFullScore);

        // F. Proximity bonus — liquidations near sweep extreme
        double proximityBonus = (nearSweepCount > 0 && count > 0)
            ? Clamp01(nearSweepCount / (double)count) * 0.20
            : 0.0;

        // ── Combined score ───────────────────────────────────────
        // Weights: count 25%, qty 15%, time 20%, direction 30%, whale 10%
        double baseScore = (countScore * 0.25) + (qtyScore * 0.15) + (timeScore * 0.20)
                         + (dirScore * 0.30) + (whaleBonus / 0.15 * 0.10);
        double score = Clamp01(baseScore + proximityBonus);

        var lastTick = priceWindow[priceWindow.Length - 1];
        string dir = isBullishSweep ? "longs_stopped" : "shorts_stopped";
        string context = $"{dir}:{score:F2}:{count}:{totalQty:F1}";

        // DEBUG_LIQ: full scoring breakdown
        Console.WriteLine($"[DEBUG_LIQ] ★ LIQ CLUSTER FIRED {dir} score={score:F2} " +
            $"(cnt={countScore:F2} qty={qtyScore:F2} whale={whaleBonus:F2} " +
            $"time={timeScore:F2} dirR={dirRatio:F2}→{dirScore:F2} prox+{proximityBonus:F2})");
        // END DEBUG_LIQ

        return new CepEvent(lastTick.Timestamp, lastTick.Symbol, "LiquidationCluster", lastTick.Price, context);
    }

    // ── Scoring helpers ──────────────────────────────────────────────

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>
    /// Score time concentration: returns 0.3 (spread across full window) to 1.0 (all in one instant).
    /// </summary>
    private static double ScoreTimeConcentration(List<long> timestamps, long windowStart, long windowEnd)
    {
        if (timestamps.Count < 2) return 0.8; // single liq = moderately concentrated by definition

        long actualSpan = timestamps.Max() - timestamps.Min();
        long windowSpan = windowEnd - windowStart;
        if (windowSpan <= 0) return 0.5;

        // concentration = how much of the window is NOT used (1.0 = all at same time)
        double spreadRatio = Math.Min((double)actualSpan / windowSpan, 1.0);
        double concentration = 1.0 - spreadRatio;

        // Remap: 0.0 concentration → 0.3, 1.0 concentration → 1.0
        return 0.3 + concentration * 0.7;
    }
}
