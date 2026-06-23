using CEPx.Core;

namespace CEPx.Scoring;

/// <summary>
/// Per-structure scoring functions for the market-structure scoring layer.
/// Each method takes a price window + optional event/detector output,
/// normalizes features against ScoringConfig targets, and returns [0, 1].
///
/// Phase 2: 6 scorers implemented — faithful extraction of detector scoring logic.
/// Phase 3: Remaining 3 scorers (BreakoutFail, PullbackResume, LowLiquidityReject).
/// </summary>
public static class StructureScorers
{
    // ═══════════════════════════════════════════════════════════════
    // ── Reversal structures ──────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score sweep + reclaim: price crosses back past sweep origin.
    /// Computed from window + sweep origin since ReclaimDetector has no internal score.
    ///
    /// Three dimensions:
    ///   A. Reclaim depth — how far past origin did price reclaim? (0.3% of price → 1.0)
    ///   B. Reclaim speed — fewer ticks to reclaim = faster = stronger (1 tick → 1.0, 10+ → 0.1)
    ///   C. Post-reclaim hold — did price stay on reclaimed side? (≥2 ticks → 1.0)
    /// Weights: depth 50%, speed 30%, hold 20%.
    /// </summary>
    public static double ScoreSweepReclaim(
        MarketEvent[] priceWindow,
        double sweepOrigin,
        bool isBullishSweep,
        CepEvent? reclaimEvent,
        ScoringConfig cfg)
    {
        if (reclaimEvent == null) return 0.0;
        if (priceWindow.Length < 2) return 0.2; // reclaim fired but too few ticks to score

        int n = priceWindow.Length;

        // Find the reclaim tick index (closest price to reclaimEvent.Price)
        int reclaimIdx = -1;
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(priceWindow[i].Price - reclaimEvent.Value.Price) < 1e-8
                || Math.Abs(priceWindow[i].Timestamp - reclaimEvent.Value.Timestamp) < 10)
            { reclaimIdx = i; break; }
        }
        if (reclaimIdx < 0) reclaimIdx = n - 1; // fallback: assume last tick

        // ── A. Reclaim depth: how far past origin (as % of price) ──
        double reclaimPrice = priceWindow[reclaimIdx].Price;
        double reclaimDepthPct = Math.Abs(reclaimPrice - sweepOrigin) / sweepOrigin * 100.0;
        double depthScore = Normalize(reclaimDepthPct, cfg.ReclaimDepthNormPct);

        // ── B. Reclaim speed: fewer ticks = more decisive ──────────
        // reclaimIdx + 1 = ticks from start to reclaim
        int ticksToReclaim = reclaimIdx + 1;
        double speedScore = ticksToReclaim <= 1 ? 1.0
                          : ticksToReclaim <= 3 ? 0.8
                          : ticksToReclaim <= 5 ? 0.5
                          : ticksToReclaim <= 8 ? 0.3
                          : 0.1;

        // ── C. Post-reclaim hold: does price stay reclaimed? ────────
        int holdTicks = 0;
        for (int i = reclaimIdx + 1; i < n; i++)
        {
            bool stillReclaimed = isBullishSweep
                ? priceWindow[i].Price < sweepOrigin   // bullish sweep: reclaimed = below origin
                : priceWindow[i].Price > sweepOrigin;  // bearish sweep: reclaimed = above origin
            if (stillReclaimed) holdTicks++;
            else break;
        }
        double holdScore = holdTicks >= 2 ? 1.0 : holdTicks >= 1 ? 0.6 : 0.2;

        // ── Combined ──────────────────────────────────────────────
        return Clamp01(depthScore * 0.50 + speedScore * 0.30 + holdScore * 0.20);
    }

    /// <summary>
    /// Score breakout fail: price breaks a prior swing level but closes back inside.
    /// No existing detector — new structure.
    /// </summary>
    public static double ScoreBreakoutFail(
        MarketEvent[] priceWindow,
        bool isBullishSweep,
        ScoringConfig cfg)
    {
        // STUB — Phase 3
        return 0.0;
    }

    /// <summary>
    /// Score exhaustion after extension: momentum deceleration.
    /// Faithful extraction of ExhaustionDetector.CheckExhaustion() scoring logic.
    ///
    /// Four dimensions (same weights as detector):
    ///   A. Deceleration ratio — second/first half range (0.15 → 1.0, threshold → 0.0)  [45%]
    ///   B. Initial move magnitude — bigger impulse = more significant exhaustion      [20%]
    ///   C. Reversal lean — does final price lean against sweep direction?             [25%]
    ///   D. Time progression — more ticks in window = more credible                    [10%]
    /// </summary>
    public static double ScoreExhaustion(
        MarketEvent[] priceWindow,
        double sweepPrice,
        bool isBullishSweep,
        CepEvent? exhaustionEvent,
        ScoringConfig cfg)
    {
        // ── Reproducible from window even without the event, but respect null event ──
        int n = priceWindow.Length;
        if (n < 4) return 0.0;

        int half = n / 2;

        // ── Compute halves ───────────────────────────────────────
        double firstMin = double.MaxValue, firstMax = double.MinValue;
        for (int i = 0; i < half; i++)
        {
            if (priceWindow[i].Price < firstMin) firstMin = priceWindow[i].Price;
            if (priceWindow[i].Price > firstMax) firstMax = priceWindow[i].Price;
        }
        double firstRange = (firstMax - firstMin) / sweepPrice * 100;

        double secondMin = double.MaxValue, secondMax = double.MinValue;
        for (int i = half; i < n; i++)
        {
            if (priceWindow[i].Price < secondMin) secondMin = priceWindow[i].Price;
            if (priceWindow[i].Price > secondMax) secondMax = priceWindow[i].Price;
        }
        double secondRange = (secondMax - secondMin) / sweepPrice * 100;

        if (firstRange <= 0) return 0.0;
        double ratio = secondRange / firstRange;

        // ── A. Deceleration ratio (lower = stronger) ──────────────
        // Uses ScoringConfig.DecelerationNorm (maps to ExhaustionStallNorm = 0.15)
        double decelScore = Clamp01(1.0 - (ratio / cfg.DecelerationNorm));

        // ── B. Initial move magnitude ─────────────────────────────
        double magScore = Normalize(firstRange, cfg.InitialMoveNormPct);

        // ── C. Reversal lean ──────────────────────────────────────
        double reversalScore = ScoreReversalLean(
            priceWindow[0].Price, priceWindow[n - 1].Price, isBullishSweep);

        // ── D. Time progression (more ticks = higher confidence) ──
        double progressScore = Clamp01((double)n / 6.0); // detector default window = 6

        // ── Combined (same weights as ExhaustionDetector) ────────
        return Clamp01(decelScore * 0.45 + magScore * 0.20
                     + reversalScore * 0.25 + progressScore * 0.10);
    }

    /// <summary>
    /// Score absorption after aggressive move: high volume + minimal price change.
    /// Faithful extraction of AbsorptionAfterSweepDetector.CheckAbsorption() scoring logic.
    ///
    /// Three dimensions (same weights as detector):
    ///   A. Volume intensity — (volRatio - 3.0) / (8.0 - 3.0) → [0,1]       [40%]
    ///   B. Price stability — 1.0 - (pctMove / maxMove) → [0,1]              [35%]
    ///   C. Volume concentration — (share - expected) / (1 - expected) [25%]
    /// </summary>
    public static double ScoreAbsorption(
        MarketEvent[] priceWindow,
        double sweepPrice,
        CepEvent? absorptionEvent,
        ScoringConfig cfg)
    {
        if (absorptionEvent == null || priceWindow.Length < 2) return 0.0;

        int n = priceWindow.Length;

        // ── Recompute stats from window (matches detector logic) ──
        double avgVol = 0, maxVol = 0;
        for (int i = 0; i < n - 1; i++)
        {
            avgVol += priceWindow[i].Volume;
            if (priceWindow[i].Volume > maxVol) maxVol = priceWindow[i].Volume;
        }
        avgVol /= (n - 1);

        var last = priceWindow[n - 1];
        if (last.Volume > maxVol) maxVol = last.Volume;

        double volRatio = last.Volume / Math.Max(avgVol, 1e-10);

        double pctMove = Math.Abs(last.Price - sweepPrice) / sweepPrice * 100;

        // ── A. Volume intensity: 3x→0.0, 8x→1.0 (detector defaults) ──
        const double volMin = 3.0;  // AbsorptionVolumeMultiplier
        double volScore = Clamp01((volRatio - volMin) / (cfg.VolIntensityNorm - volMin));

        // ── B. Price stability: 0%→1.0, max%→0.0 ────────────────
        const double maxMovePct = 0.1; // AbsorptionMaxPriceMovePct
        double stabilityScore = Clamp01(1.0 - (pctMove / maxMovePct));

        // ── C. Volume concentration ──────────────────────────────
        double totalVol = avgVol * (n - 1) + last.Volume;
        double concentration = totalVol > 0 ? last.Volume / totalVol : 0;
        double expectedShare = 1.0 / n;
        double concentrationScore = Clamp01((concentration - expectedShare) / (1.0 - expectedShare));

        // ── Combined (same weights as AbsorptionAfterSweepDetector) ──
        return Clamp01(volScore * 0.40 + stabilityScore * 0.35 + concentrationScore * 0.25);
    }

    /// <summary>
    /// Score liquidation cluster at extreme: liquidations against sweep direction.
    /// This scorer parses the event context since LiqCluster scoring requires
    /// the raw LiquidationEvent[] array (not available in CepEvent).
    ///
    /// When liqClusterEvent is present, parse score from context.
    /// When absent, return 0.0.
    ///
    /// NOTE: Full recomputation from raw LiquidationEvent[] data is deferred to Phase 4
    /// wiring when the scorer has direct access to the liquidation array.
    /// </summary>
    public static double ScoreLiquidationCluster(
        CepEvent? liqClusterEvent)
    {
        if (liqClusterEvent == null) return 0.0;

        // Parse score from context: "{dir}:{score}:{count}:{qty}" e.g. "longs_stopped:0.72:5:35.2"
        return ParseLiqScoreFromContext(liqClusterEvent.Value.Context);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Continuation structures ──────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score momentum persistence: sustained directional movement.
    /// Faithful extraction of MomentumPersistenceDetector.Detect() scoring logic.
    ///
    /// Four dimensions (same weights as detector):
    ///   A. Direction consistency — (ratio - 0.5) / 0.5 → [0,1]              [40%]
    ///   B. Net move magnitude — pct / moveNorm → [0,1]                      [30%]
    ///   C. Velocity stability — CV-based smoothness score                    [20%]
    ///   D. Kalman velocity alignment — agrees with direction?                 [10%]
    /// </summary>
    public static double ScoreMomentumPersistence(
        MarketEvent[] priceWindow,
        double kalmanVelocity,
        CepEvent? momentumEvent,
        ScoringConfig cfg)
    {
        int n = priceWindow.Length;
        if (n < 2) return 0.0;

        double firstPrice = priceWindow[0].Price;
        double lastPrice = priceWindow[n - 1].Price;

        // ── Pass 1: tick-to-tick returns and direction ────────────
        int upCount = 0, downCount = 0;
        var returns = new List<double>();
        for (int i = 1; i < n; i++)
        {
            double ret = (priceWindow[i].Price - priceWindow[i - 1].Price) / priceWindow[i - 1].Price;
            returns.Add(ret);
            if (ret > 1e-8) upCount++;
            else if (ret < -1e-8) downCount++;
        }
        if (returns.Count == 0) return 0.0;

        bool isUp = upCount > downCount;
        int dominantCount = isUp ? upCount : downCount;
        double directionConsistency = (double)dominantCount / returns.Count;

        double netMovePct = Math.Abs((lastPrice - firstPrice) / firstPrice * 100.0);

        // ── A. Direction consistency: 0.5→0.0, 0.75→0.5, 1.0→1.0 ──
        double consistencyScore = Clamp01((directionConsistency - 0.5) / 0.5);

        // ── B. Net move magnitude ─────────────────────────────────
        double moveScore = Normalize(netMovePct, cfg.NetMoveNormPct);

        // ── C. Velocity stability (CV-based) ──────────────────────
        double meanAbsRet = returns.Average(r => Math.Abs(r));
        double stabilityScore = ScoreVelocityStability(returns, meanAbsRet);

        // ── D. Kalman velocity alignment ──────────────────────────
        double kalmanScore = 0.5;
        if (Math.Abs(kalmanVelocity) > 1e-8)
        {
            bool kalmanAgrees = (isUp && kalmanVelocity > 0) || (!isUp && kalmanVelocity < 0);
            if (kalmanAgrees)
            {
                double kalmanStrength = Clamp01(Math.Abs(kalmanVelocity) / 2.0);
                kalmanScore = 0.5 + kalmanStrength * 0.5;
            }
            else kalmanScore = 0.2;
        }

        // ── Combined (same weights as MomentumPersistenceDetector) ──
        return Clamp01(consistencyScore * 0.40 + moveScore * 0.30
                     + stabilityScore * 0.20 + kalmanScore * 0.10);
    }

    /// <summary>
    /// Score clean continuation: directional move without opposing absorption.
    /// Faithful extraction of NoMeaningfulAbsorptionDetector.Detect() scoring logic.
    ///
    /// Three dimensions (same weights as detector):
    ///   A. Price continuation — pct / moveNorm → [0,1]                        [50%]
    ///   B. Volume containment — 1.0 - (volRatio-1) / (threshold-1) → [0,1]   [30%]
    ///   C. Movement smoothness — fraction of aligned ticks                     [20%]
    /// </summary>
    public static double ScoreCleanContinuation(
        MarketEvent[] priceWindow,
        CepEvent? cleanContEvent,
        ScoringConfig cfg)
    {
        int n = priceWindow.Length;
        if (n < 2) return 0.0;

        double firstPrice = priceWindow[0].Price;
        double lastPrice = priceWindow[n - 1].Price;

        // ── Pass 1: collect stats ────────────────────────────────
        double totalVolume = 0, maxVolume = 0;
        double maxVolumePriceMove = 0;
        int maxVolumeIndex = 0;
        for (int i = 0; i < n; i++)
        {
            var tick = priceWindow[i];
            totalVolume += tick.Volume;
            if (tick.Volume > maxVolume) { maxVolume = tick.Volume; maxVolumeIndex = i; }
        }
        double avgVolume = totalVolume / n;

        if (maxVolumeIndex > 0)
        {
            maxVolumePriceMove = Math.Abs(
                (priceWindow[maxVolumeIndex].Price - priceWindow[maxVolumeIndex - 1].Price)
                / priceWindow[maxVolumeIndex - 1].Price * 100.0);
        }

        double netMovePct = Math.Abs((lastPrice - firstPrice) / firstPrice * 100.0);
        bool isUp = lastPrice > firstPrice;

        double volRatio = maxVolume / Math.Max(avgVolume, 1e-10);

        // ── Gate: if absorption IS present → return 0 (signal absent) ──
        const double volThreshold = 2.5; // NoAbsorptionMaxVolumeMultiplier
        bool hasAbsorption = volRatio >= volThreshold && maxVolumePriceMove < 0.05;
        if (hasAbsorption) return 0.0;

        // ── A. Price continuation ─────────────────────────────────
        double continuationScore = Normalize(netMovePct, cfg.NetMoveNormPct);

        // ── B. Volume containment (below threshold = good) ────────
        double containmentScore = Clamp01(1.0 - (volRatio - 1.0) / (volThreshold - 1.0));

        // ── C. Movement smoothness ────────────────────────────────
        double smoothnessScore = ScoreMovementSmoothness(priceWindow, isUp);

        // ── Combined (same weights as NoMeaningfulAbsorptionDetector) ──
        return Clamp01(continuationScore * 0.50 + containmentScore * 0.30 + smoothnessScore * 0.20);
    }

    /// <summary>
    /// Score continuation after pullback: price retraces 30-70% then resumes.
    /// No existing detector — new structure.
    /// </summary>
    public static double ScorePullbackResume(
        MarketEvent[] priceWindow,
        double sweepOrigin,
        bool isBullishSweep,
        ScoringConfig cfg)
    {
        // STUB — Phase 3
        return 0.0;
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Direction-agnostic structures ────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score low-liquidity rejection/acceptance: thin-volume sweep behavior.
    /// Uses Volume + BidSize/AskSize when available.
    /// </summary>
    public static double ScoreLowLiquidityReject(
        MarketEvent[] priceWindow,
        double dailyAvgVolume,
        ScoringConfig cfg)
    {
        // STUB — Phase 3
        return 0.0;
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Utility ──────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Clamp value to [0, 1] range.</summary>
    public static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>Normalize raw feature value against config target → [0, 1].</summary>
    public static double Normalize(double rawValue, double normTarget)
        => normTarget > 0 ? Clamp01(rawValue / normTarget) : 0.0;

    // ── Private scoring helpers (extracted from detectors) ──────────

    /// <summary>
    /// Score reversal lean: how much did price move against the sweep direction?
    /// Returns 0.3 (continued with sweep) to 1.0 (strong reversal).
    /// Identical to ExhaustionDetector.ScoreReversalLean().
    /// </summary>
    private static double ScoreReversalLean(double firstPrice, double lastPrice, bool isBullishSweep)
    {
        double pctChange = (lastPrice - firstPrice) / firstPrice * 100;
        if (isBullishSweep)
        {
            if (pctChange <= -0.2) return 1.0;
            if (pctChange <= -0.1) return 0.8;
            if (pctChange <= 0.0) return 0.6;
            if (pctChange <= 0.1) return 0.4;
            return 0.3;
        }
        else
        {
            if (pctChange >= 0.2) return 1.0;
            if (pctChange >= 0.1) return 0.8;
            if (pctChange >= 0.0) return 0.6;
            if (pctChange >= -0.1) return 0.4;
            return 0.3;
        }
    }

    /// <summary>
    /// Score velocity stability: CV < 0.5 → 1.0, CV > 3.0 → 0.1.
    /// Identical to MomentumPersistenceDetector.ScoreVelocityStability().
    /// </summary>
    private static double ScoreVelocityStability(List<double> returns, double meanAbsRet)
    {
        if (meanAbsRet < 1e-10) return 0.5;
        double variance = returns.Average(r => (r - returns.Average()) * (r - returns.Average()));
        double stdDev = Math.Sqrt(Math.Abs(variance));
        double cv = stdDev / meanAbsRet;
        if (cv <= 0.5) return 1.0;
        if (cv >= 3.0) return 0.1;
        return 1.0 - (cv - 0.5) / (3.0 - 0.5) * 0.9;
    }

    /// <summary>
    /// Score movement smoothness: fraction of ticks in dominant direction.
    /// Returns 0.3 (choppy) to 1.0 (every tick aligned).
    /// Identical to NoMeaningfulAbsorptionDetector.ScoreMovementSmoothness().
    /// </summary>
    private static double ScoreMovementSmoothness(MarketEvent[] window, bool isUp)
    {
        int aligned = 0, total = 0;
        for (int i = 1; i < window.Length; i++)
        {
            total++;
            double ret = window[i].Price - window[i - 1].Price;
            bool tickUp = ret > 1e-8, tickDown = ret < -1e-8;
            if ((isUp && tickUp) || (!isUp && tickDown)) aligned++;
            else if (!tickUp && !tickDown) aligned++; // flat = half credit
        }
        if (total == 0) return 0.5;
        double ratio = (double)aligned / total;
        return Clamp01(0.3 + (ratio - 0.5) / 0.5 * 0.7);
    }

    /// <summary>
    /// Parse liquidation cluster score from context string.
    /// Format: "{dir}:{score}:{count}:{qty}" e.g. "longs_stopped:0.72:5:35.2"
    /// </summary>
    private static double ParseLiqScoreFromContext(string context)
    {
        if (string.IsNullOrEmpty(context)) return 0.5;
        var parts = context.Split(':');
        if (parts.Length >= 2 && double.TryParse(parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score))
            return score;
        return 0.5;
    }
}
