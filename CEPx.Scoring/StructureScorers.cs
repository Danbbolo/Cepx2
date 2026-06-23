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
    /// Classic trap pattern — breakout traders get caught, price reverses.
    ///
    /// Four dimensions:
    ///   A. Penetration depth — how far did price break the level? (0.2% → 1.0)     [30%]
    ///   B. Close-back speed — did it fail within 2 ticks? (1 tick → 1.0, 5+ → 0.1) [30%]
    ///   C. Volume on breakout — high vol breakout that fails = stronger trap        [20%]
    ///   D. Retracement ratio — what % of breakout was retraced? (100% → 1.0)        [20%]
    ///
    /// Lookback: uses priceWindow to find swing high/low, then checks if recent
    /// ticks broke it and closed back inside.
    /// </summary>
    public static double ScoreBreakoutFail(
        MarketEvent[] priceWindow,
        double sweepPrice,
        bool isBullishSweep,
        ScoringConfig cfg)
    {
        int n = priceWindow.Length;
        if (n < 8) return 0.0; // need enough history for swing + breakout + fail

        int half = n / 2;

        // ── Find swing level from first half of window ────────────
        double swingHigh = double.MinValue, swingLow = double.MaxValue;
        for (int i = 0; i < half; i++)
        {
            if (priceWindow[i].Price > swingHigh) swingHigh = priceWindow[i].Price;
            if (priceWindow[i].Price < swingLow) swingLow = priceWindow[i].Price;
        }
        if (swingHigh <= 0 || swingLow <= 0) return 0.0;

        // Swing range must be meaningful
        double swingRangePct = (swingHigh - swingLow) / sweepPrice * 100;
        if (swingRangePct < 0.05) return 0.0; // too narrow to be meaningful

        // ── Check for breakout in second half ────────────────────
        double breakoutLevel = isBullishSweep ? swingHigh : swingLow;
        int breakoutTick = -1;
        double maxPenetration = 0;
        int maxPenetrationTick = -1;

        for (int i = half; i < n; i++)
        {
            double pctBeyond = isBullishSweep
                ? (priceWindow[i].Price - breakoutLevel) / breakoutLevel * 100
                : (breakoutLevel - priceWindow[i].Price) / breakoutLevel * 100;

            if (pctBeyond > 0 && pctBeyond > maxPenetration)
            {
                maxPenetration = pctBeyond;
                maxPenetrationTick = i;
                if (breakoutTick < 0) breakoutTick = i;
            }
        }

        if (breakoutTick < 0) return 0.0; // no breakout — structure absent

        // ── Check for close-back (fail) after breakout ────────────
        bool closedBack = false;
        int closeBackTick = -1;
        for (int i = maxPenetrationTick + 1; i < n; i++)
        {
            bool inside = isBullishSweep
                ? priceWindow[i].Price <= breakoutLevel
                : priceWindow[i].Price >= breakoutLevel;

            if (inside) { closedBack = true; closeBackTick = i; break; }
        }

        if (!closedBack) return 0.0; // breakout still running — structure absent

        // ── A. Penetration depth ──────────────────────────────────
        double depthScore = Normalize(maxPenetration, cfg.PenetrationDepthNormPct);

        // ── B. Close-back speed ───────────────────────────────────
        int ticksToFail = closeBackTick - maxPenetrationTick;
        double speedScore = ticksToFail <= 1 ? 1.0
                          : ticksToFail <= 2 ? 0.8
                          : ticksToFail <= 3 ? 0.5
                          : ticksToFail <= 5 ? 0.3
                          : 0.1;

        // ── C. Volume on breakout — high vol fail = stronger trap ──
        double breakoutVol = priceWindow[maxPenetrationTick].Volume;
        double avgVol = 0;
        for (int i = 0; i < n; i++) avgVol += priceWindow[i].Volume;
        avgVol /= n;
        double volRatio = breakoutVol / Math.Max(avgVol, 1e-10);
        double volumeScore = Normalize(volRatio, cfg.ResumeVolumeNorm);

        // ── D. Retracement ratio — how much of the breakout reversed ──
        double reversalDepth = isBullishSweep
            ? maxPenetration > 0 ? (priceWindow[maxPenetrationTick].Price - priceWindow[closeBackTick].Price)
                                   / (priceWindow[maxPenetrationTick].Price - breakoutLevel) : 0
            : maxPenetration > 0 ? (priceWindow[closeBackTick].Price - priceWindow[maxPenetrationTick].Price)
                                   / (breakoutLevel - priceWindow[maxPenetrationTick].Price) : 0;
        double retraceScore = Clamp01(Math.Max(0, reversalDepth));

        // ── Combined ──────────────────────────────────────────────
        return Clamp01(depthScore * 0.30 + speedScore * 0.30 + volumeScore * 0.20 + retraceScore * 0.20);
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
    /// Score continuation after pullback: price retraces 30-70% of sweep range
    /// and then resumes in the sweep direction. The pullback-resume structure is a
    /// classic continuation setup — stops are run, then trend resumes.
    ///
    /// Four dimensions:
    ///   A. Retracement depth — how deep was the pullback? (30-50% healthy → 1.0, 70%+ → 0.2) [35%]
    ///   B. Resume speed — how fast did price recover? (1-2 ticks → 1.0)                    [25%]
    ///   C. New extreme — did price make a new extreme beyond sweep? (yes → 1.0)             [25%]
    ///   D. Volume on resume — high vol recovery = commitment                                [15%]
    ///
    /// Structure absent if: no retracement into 30-70% zone, or price doesn't resume.
    /// </summary>
    public static double ScorePullbackResume(
        MarketEvent[] priceWindow,
        double sweepOrigin,
        bool isBullishSweep,
        ScoringConfig cfg)
    {
        int n = priceWindow.Length;
        if (n < 6) return 0.0;

        double firstPrice = priceWindow[0].Price;

        // ── Find sweep extreme (farthest from origin in sweep direction) ──
        double sweepExtreme = firstPrice;
        int extremeIdx = 0;
        for (int i = 1; i < n; i++)
        {
            bool beyond = isBullishSweep
                ? priceWindow[i].Price > sweepExtreme
                : priceWindow[i].Price < sweepExtreme;
            if (beyond) { sweepExtreme = priceWindow[i].Price; extremeIdx = i; }
        }

        double sweepRangePct = Math.Abs(sweepExtreme - sweepOrigin) / sweepOrigin * 100;
        if (sweepRangePct < 0.08) return 0.0; // sweep too small

        // ── Find deepest retracement AFTER the extreme ────────────
        double deepestRetrace = sweepExtreme;
        int retraceIdx = extremeIdx;
        for (int i = extremeIdx + 1; i < n; i++)
        {
            bool deeper = isBullishSweep
                ? priceWindow[i].Price < deepestRetrace
                : priceWindow[i].Price > deepestRetrace;
            if (deeper) { deepestRetrace = priceWindow[i].Price; retraceIdx = i; }
        }

        double retraceDist = isBullishSweep
            ? sweepExtreme - deepestRetrace
            : deepestRetrace - sweepExtreme;
        double retracePct = sweepExtreme > 0
            ? retraceDist / sweepExtreme * 100 : 0;
        double sweepDist = isBullishSweep
            ? sweepExtreme - sweepOrigin
            : sweepOrigin - sweepExtreme;
        double retraceRatio = sweepDist > 0 ? retraceDist / sweepDist : 0;

        // ── Gate: retracement must be 30-70% of sweep range ──────
        if (retraceRatio < 0.30 || retraceRatio > 0.85) return 0.0;

        // ── Check for resume: price moves back toward sweep extreme ──
        double resumePrice = deepestRetrace;
        int resumeIdx = retraceIdx;
        for (int i = retraceIdx + 1; i < n; i++)
        {
            bool resuming = isBullishSweep
                ? priceWindow[i].Price > resumePrice
                : priceWindow[i].Price < resumePrice;
            if (resuming) { resumePrice = priceWindow[i].Price; resumeIdx = i; }
        }

        if (resumeIdx <= retraceIdx) return 0.0; // no resume — structure absent

        // ── A. Retracement depth quality (30-50% = healthiest) ────
        // Peak score at 40% retracement (healthy pullback), decays to 70%
        double healthyMax = cfg.RetracementHealthyMaxPct; // default 0.5
        double retraceQuality = retraceRatio <= healthyMax
            ? Clamp01(retraceRatio / healthyMax)
            : Clamp01(1.0 - (retraceRatio - healthyMax) / (0.85 - healthyMax));
        double retraceScore = retraceQuality * 0.7 + 0.3; // floor 0.3 if in zone

        // ── B. Resume speed ───────────────────────────────────────
        int resumeTicks = resumeIdx - retraceIdx;
        double speedScore = resumeTicks <= 1 ? 1.0
                          : resumeTicks <= 2 ? 0.8
                          : resumeTicks <= 4 ? 0.5
                          : resumeTicks <= 6 ? 0.3
                          : 0.1;

        // ── C. New extreme — did price break beyond sweep extreme? ──
        double resumeMove = isBullishSweep
            ? resumePrice - deepestRetrace
            : deepestRetrace - resumePrice;
        double resumeMovePct = deepestRetrace > 0
            ? resumeMove / deepestRetrace * 100 : 0;
        bool madeNewExtreme = isBullishSweep
            ? resumePrice > sweepExtreme * 1.0005
            : resumePrice < sweepExtreme * 0.9995;
        double extremeScore = madeNewExtreme ? 1.0 : Clamp01(resumeMovePct / (sweepRangePct * 0.5));

        // ── D. Volume on resume ───────────────────────────────────
        double resumeVol = 0;
        int volCount = 0;
        for (int i = retraceIdx + 1; i <= resumeIdx; i++)
        {
            resumeVol += priceWindow[i].Volume;
            volCount++;
        }
        double avgResumeVol = volCount > 0 ? resumeVol / volCount : 0;
        double avgAllVol = 0;
        for (int i = 0; i < n; i++) avgAllVol += priceWindow[i].Volume;
        avgAllVol /= n;
        double resumeVolRatio = avgAllVol > 0 ? avgResumeVol / avgAllVol : 1.0;
        double volScore = Normalize(resumeVolRatio, cfg.ResumeVolumeNorm);

        // ── Combined ──────────────────────────────────────────────
        return Clamp01(retraceScore * 0.35 + speedScore * 0.25
                     + extremeScore * 0.25 + volScore * 0.15);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Direction-agnostic structures ────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score low-liquidity rejection/acceptance: thin-volume sweep behavior.
    /// When volume is unusually low, sweeps are more likely to be "fake" —
    /// either rejected (long wick opposite direction) or accepted (clean follow-through).
    ///
    /// Three dimensions:
    ///   A. Volume thinness — how far below daily avg? (<50% → 1.0, >80% → 0.0)     [35%]
    ///   B. Wick-to-body ratio — long wick against sweep = rejection signal           [35%]
    ///   C. Follow-through — next ticks continue or reverse? (continue → 1.0 cont)    [30%]
    ///
    /// The score can be interpreted as:
    ///   - High score with wick → REJECTION (reversal signal)
    ///   - High score with follow-through → ACCEPTANCE (continuation signal)
    ///   - Low score → normal liquidity, this structure doesn't apply
    ///
    /// Uses BidSize/AskSize from MarketEvent as bonus when available (CHD data).
    /// </summary>
    public static double ScoreLowLiquidityReject(
        MarketEvent[] priceWindow,
        double sweepOrigin,
        bool isBullishSweep,
        double dailyAvgVolume,
        ScoringConfig cfg)
    {
        int n = priceWindow.Length;
        if (n < 4 || dailyAvgVolume <= 0) return 0.0;

        // ── A. Volume thinness ────────────────────────────────────
        double windowAvgVol = 0;
        for (int i = 0; i < n; i++) windowAvgVol += priceWindow[i].Volume;
        windowAvgVol /= n;

        double thinRatio = windowAvgVol / dailyAvgVolume;
        if (thinRatio > 0.9) return 0.0; // normal volume — structure absent

        // thinRatio at ThinVolumeRatio (0.5) → score 0.5, at 0.2 → score 1.0
        double thinnessScore = Clamp01(1.0 - (thinRatio / cfg.ThinVolumeRatio));

        // ── B. Wick-to-body ratio (on the sweep candle) ────────────
        // Sweep candle = first tick of window (when sweep was detected)
        double sweepOpen = priceWindow[0].Price;
        double sweepHigh = sweepOpen, sweepLow = sweepOpen;
        double sweepVol = priceWindow[0].Volume;

        // Find the sweep candle's high/low in the first few ticks
        for (int i = 0; i < Math.Min(3, n); i++)
        {
            if (priceWindow[i].Price > sweepHigh) sweepHigh = priceWindow[i].Price;
            if (priceWindow[i].Price < sweepLow) sweepLow = priceWindow[i].Price;
        }

        double body = Math.Abs(sweepOpen - priceWindow[Math.Min(2, n - 1)].Price);
        double totalRange = sweepHigh - sweepLow;
        double wickRatio = body > 1e-10 ? (totalRange - body) / body : totalRange > 1e-10 ? 3.0 : 0;

        // Wick direction matters: wick against sweep = rejection
        double upperWick = sweepHigh - Math.Max(sweepOpen, priceWindow[Math.Min(2, n - 1)].Price);
        double lowerWick = Math.Min(sweepOpen, priceWindow[Math.Min(2, n - 1)].Price) - sweepLow;
        bool wickAgainstSweep = isBullishSweep
            ? upperWick > lowerWick * 1.5   // bullish sweep: upper wick = rejection
            : lowerWick > upperWick * 1.5;   // bearish sweep: lower wick = rejection

        double wickScore = Normalize(wickRatio, cfg.WickBodyRatioNorm);
        if (!wickAgainstSweep) wickScore *= 0.3; // wrong-direction wick = weak signal

        // ── C. Follow-through (rest of window after sweep) ────────
        int followStart = Math.Min(3, n);
        if (followStart >= n) return Clamp01(thinnessScore * 0.35 + wickScore * 0.35);

        int continueTicks = 0, reverseTicks = 0, totalFollow = 0;
        for (int i = followStart; i < n; i++)
        {
            totalFollow++;
            double move = priceWindow[i].Price - priceWindow[i - 1].Price;
            bool movingWithSweep = isBullishSweep ? move > 0 : move < 0;

            if (movingWithSweep) continueTicks++;
            else if (Math.Abs(move) > 1e-8) reverseTicks++;
        }

        double followRatio = totalFollow > 0 ? (double)continueTicks / totalFollow : 0.5;

        // Follow-through score: binary interpretation
        // > 60% continuation → acceptance signal (cont)
        // < 40% continuation → rejection signal (rev)
        // 40-60% → neutral
        double followScore;
        if (followRatio >= 0.6) followScore = Clamp01((followRatio - 0.5) / 0.5); // 0.5→0.0, 1.0→1.0
        else if (followRatio <= 0.4) followScore = 0.0; // reversal follow-through
        else followScore = 0.3; // neutral

        // ── D. BidSize/AskSize bonus (CHD data only) ──────────────
        double bookBonus = 0.0;
        double totalBid = 0, totalAsk = 0;
        for (int i = 0; i < n; i++) { totalBid += priceWindow[i].BidSize; totalAsk += priceWindow[i].AskSize; }
        if (totalBid > 0 && totalAsk > 0)
        {
            double ratio = totalBid / totalAsk;
            double imbalance = Math.Abs(ratio - 1.0);
            bookBonus = Normalize(imbalance, cfg.BidAskRatioNorm) * 0.15;
        }

        // ── Combined ──────────────────────────────────────────────
        // Note: the combined score reflects the confidence that this
        // thin-volume sweep is MEANINGFUL (rejection or acceptance).
        // The DIRECTION is encoded in wickScore and followScore.
        return Clamp01(thinnessScore * 0.35 + wickScore * 0.35
                     + followScore * 0.30 + bookBonus);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Phase C: New structure scorers ───────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score consolidation breakout: tight range + thin volume = coil before move.
    /// Continuation signal — the breakout direction is the existing trend.
    /// </summary>
    public static double ScoreConsolidationBreakout(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        if (events.Consolidation == null) return 0.0;
        double baseScore = ParseScoreFromContext(events.Consolidation.Value.Context);
        // Boost if volume is thin (coil more likely to break)
        if (events.IsThinVolume) baseScore = Clamp01(baseScore + 0.10);
        return baseScore;
    }

    /// <summary>
    /// Score trend continuation: swing direction intact (HH/HL or LL/LH sequence).
    /// If LastSwingDirection aligns with sweep direction, trend is healthy.
    /// </summary>
    public static double ScoreTrendContinuation(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        bool trendAligned = (events.IsBullishSweep && events.LastSwingDirection == 1)
                         || (!events.IsBullishSweep && events.LastSwingDirection == -1);
        if (!trendAligned) return 0.0;
        // Score based on swing range magnitude (larger swings = stronger trend)
        double rangePct = events.CurrentSwingRange / Math.Max(events.SwingLow, 1) * 100;
        return Normalize(rangePct, 0.5); // 0.5% swing range = score 1.0
    }

    /// <summary>
    /// Score double top / double bottom: two equal swing points → reversal.
    /// </summary>
    public static double ScoreDoubleStructure(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        if (events.DoubleStructure == null) return 0.0;
        return ParseScoreFromContext(events.DoubleStructure.Value.Context);
    }

    /// <summary>
    /// Score BOS (Break of Structure): price broke a prior swing level.
    /// BOS is a continuation signal when aligned with sweep direction.
    /// Uses BOS timestamp recency with tighter window to avoid stale signals.
    /// </summary>
    public static double ScoreBOS(
        ActiveEventSnapshot events, MarketEvent[] priceWindow, ScoringConfig cfg)
    {
        if (events.BOSTimestamp <= 0) return 0.0;
        long nowMs = priceWindow.Length > 0 ? priceWindow[^1].Timestamp : 0;
        long bosAgeMs = nowMs - events.BOSTimestamp;
        const long BOS_RECENT_MS = 120_000; // 2 minutes (was 10 min — too permissive)
        if (bosAgeMs > BOS_RECENT_MS) return 0.0;

        // ── Minimum penetration gate ──────────────────────────────
        double penetrationPct = events.SwingHigh > 0 && events.BOSPrice > 0
            ? Math.Abs(events.BOSPrice - (events.IsBullishSweep ? events.SwingHigh : events.SwingLow))
              / (events.IsBullishSweep ? events.SwingHigh : events.SwingLow) * 100
            : 0;
        if (penetrationPct < 0.05) return 0.0; // require meaningful break

        double depthScore = Normalize(penetrationPct, 0.2);
        double recencyScore = Clamp01(1.0 - (double)bosAgeMs / BOS_RECENT_MS);

        bool bosAlignedWithSweep = (events.IsBullishSweep && events.BullishBOS)
                                || (!events.IsBullishSweep && events.BearishBOS);
        double baseScore = depthScore * 0.5 + recencyScore * 0.3;
        if (!bosAlignedWithSweep) baseScore *= 0.3;

        return Clamp01(baseScore + 0.05); // baseline 0.05 (was 0.10 — too generous)
    }

    /// <summary>
    /// Score CHoCH (Change of Character): BOS that reversed immediately.
    /// Strong reversal signal — the breakout was a trap.
    /// </summary>
    public static double ScoreCHoCH(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        bool chochAligned = (events.IsBullishSweep && events.BearishCHoCH)  // bullish sweep → bearish CHoCH = reversal
                         || (!events.IsBullishSweep && events.BullishCHoCH); // bearish sweep → bullish CHoCH = reversal
        if (!chochAligned) return 0.0;
        // CHoCH is a binary high-conviction signal
        return 0.75;
    }

    /// <summary>
    /// Score stop hunt: engineered liquidity grab at swing level.
    /// Strong reversal signal — trapped traders fuel the reversal.
    /// </summary>
    public static double ScoreStopHunt(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        if (events.StopHunt == null) return 0.0;
        double baseScore = ParseScoreFromContext(events.StopHunt.Value.Context);
        // Boost if volume expanded on reversal
        if (events.IsVolumeExpanding) baseScore = Clamp01(baseScore + 0.10);
        return baseScore;
    }

    /// <summary>
    /// Meta-score from volume context and regime alignment.
    /// Applies to BOTH continuation and reversal.
    /// </summary>
    public static double ComputeMetaScore(
        ActiveEventSnapshot events, ScoringConfig cfg)
    {
        double score = 0.0;
        // Volume expansion = market is active (good for any entry)
        if (events.IsVolumeExpanding) score += 0.30;
        // Normal volume = neutral
        else if (!events.IsThinVolume) score += 0.15;
        // Thin volume = low conviction
        // Volume ratio bonus
        score += Normalize(events.VolumeRatio, 2.0) * 0.20;
        return Clamp01(score);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Aggregation: ScoreMarket ─────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Main scoring entry point. Calls all structure scorers (legacy + new),
    /// groups by family, applies meta-filters, and computes directional convictions.
    /// </summary>
    public static MarketStructureScore ScoreMarket(
        MarketEvent[] priceWindow,
        ActiveEventSnapshot events,
        ScoringConfig? config = null)
    {
        var cfg = config ?? new ScoringConfig();

        // ═══════════════════════════════════════════════════════════
        // ── Call all scorers ──────────────────────────────────────
        // ═══════════════════════════════════════════════════════════

        // ── Continuation scorers (C1–C5) ──────────────────────────
        double momentumPersistScore = ScoreMomentumPersistence(priceWindow,
            events.KalmanVelocity, events.MomentumPersistence, cfg);
        double cleanContScore = ScoreCleanContinuation(priceWindow,
            events.CleanContinuation, cfg);
        double pullbackResumeScore = ScorePullbackResume(priceWindow,
            events.SweepOrigin, events.IsBullishSweep, cfg);
        double consolidationScore = ScoreConsolidationBreakout(events, cfg);
        double trendContScore = ScoreTrendContinuation(events, cfg);

        // ── Reversal scorers (R1–R6) ─────────────────────────────
        double sweepReclaimScore = ScoreSweepReclaim(priceWindow,
            events.SweepOrigin, events.IsBullishSweep, events.Reclaim, cfg);
        double exhaustionScore = ScoreExhaustion(priceWindow,
            events.SweepOrigin, events.IsBullishSweep, events.Exhaustion, cfg);
        double absorptionScore = ScoreAbsorption(priceWindow,
            events.SweepOrigin, events.Absorption, cfg);
        double liqClusterScore = ScoreLiquidationCluster(events.LiquidationCluster);
        double breakoutFailScore = ScoreBreakoutFail(priceWindow,
            events.SweepOrigin, events.IsBullishSweep, cfg);
        double doubleStructScore = ScoreDoubleStructure(events, cfg);

        // ── Structure/BOS scorers (S1–S2) ────────────────────────
        double bosScore = ScoreBOS(events, priceWindow, cfg);
        double chochScore = ScoreCHoCH(events, cfg);

        // ── Manipulation/Liquidity scorers (M1–M3) ───────────────
        double stopHuntScore = ScoreStopHunt(events, cfg);
        double lowLiqRejectScore = ScoreLowLiquidityReject(priceWindow,
            events.SweepOrigin, events.IsBullishSweep, events.DailyAvgVolume, cfg);

        // ── Meta score ───────────────────────────────────────────
        double metaScore = ComputeMetaScore(events, cfg);

        // ═══════════════════════════════════════════════════════════
        // ── Determine active structures ───────────────────────────
        // ═══════════════════════════════════════════════════════════
        StructureFlags flags = StructureFlags.None;
        if (sweepReclaimScore > 0)    flags |= StructureFlags.SweepReclaim;
        if (breakoutFailScore > 0)    flags |= StructureFlags.BreakoutFail;
        if (exhaustionScore > 0)      flags |= StructureFlags.Exhaustion;
        if (absorptionScore > 0)      flags |= StructureFlags.Absorption;
        if (liqClusterScore > 0)      flags |= StructureFlags.LiquidationCluster;
        if (momentumPersistScore > 0) flags |= StructureFlags.MomentumPersistence;
        if (cleanContScore > 0)       flags |= StructureFlags.CleanContinuation;
        if (pullbackResumeScore > 0)  flags |= StructureFlags.PullbackResume;
        if (lowLiqRejectScore > 0)    flags |= StructureFlags.LowLiquidityReject;
        if (consolidationScore > 0)   flags |= StructureFlags.ConsolidationActive;
        if (doubleStructScore > 0)    flags |= StructureFlags.DoubleStructure;
        if (stopHuntScore > 0)        flags |= StructureFlags.StopHunt;
        if (trendContScore > 0)       flags |= StructureFlags.TrendContinuation;
        if (bosScore > 0 && events.BullishBOS)  flags |= StructureFlags.BullishBOSFlag;
        if (bosScore > 0 && events.BearishBOS)  flags |= StructureFlags.BearishBOSFlag;
        if (chochScore > 0 && events.BullishCHoCH) flags |= StructureFlags.BullishCHoCHFlag;
        if (chochScore > 0 && events.BearishCHoCH) flags |= StructureFlags.BearishCHoCHFlag;

        // ═══════════════════════════════════════════════════════════
        // ── Family-based conviction ───────────────────────────────
        // ═══════════════════════════════════════════════════════════

        // Continuation family: best of C1–C5 + BOS aligned
        double familyCont = new[] { momentumPersistScore, cleanContScore,
            pullbackResumeScore, consolidationScore, trendContScore }.Max();
        double familyBOS = bosScore; // BOS is continuation-aligned

        // Reversal family: best of R1–R6
        double familyRev = new[] { sweepReclaimScore, exhaustionScore,
            absorptionScore, liqClusterScore, breakoutFailScore,
            doubleStructScore }.Max();

        // Manipulation family: best of M1–M3
        double familyManip = new[] { stopHuntScore, lowLiqRejectScore }.Max();

        // Continuation conviction
        double contConviction = Clamp01(
            familyCont * 0.40 + familyBOS * 0.25 + metaScore * cfg.MetaWeight);

        // Reversal conviction
        double revConviction = Clamp01(
            familyRev * cfg.SweepReclaimWeight / 0.25 * 0.35   // scale to family weight
            + chochScore * cfg.CHoCHWeight
            + familyManip * 0.20
            + metaScore * cfg.MetaWeight);

        // Combo bonus: ≥2 structures from different families
        int activeFamilies = (familyCont > 0 ? 1 : 0) + (familyRev > 0 ? 1 : 0)
                           + (familyManip > 0 ? 1 : 0) + (chochScore > 0 ? 1 : 0);
        if (activeFamilies >= 2)
        {
            contConviction = Clamp01(contConviction + cfg.ComboBonus);
            revConviction = Clamp01(revConviction + cfg.ComboBonus);
        }

        // ═══════════════════════════════════════════════════════════
        // ── Build and return ──────────────────────────────────────
        // ═══════════════════════════════════════════════════════════
        return new MarketStructureScore(
            timestamp: priceWindow.Length > 0 ? priceWindow[^1].Timestamp : 0,
            symbol: priceWindow.Length > 0 ? priceWindow[^1].Symbol : "",
            stateMean: 0, stateVelocity: events.KalmanVelocity,
            uncertaintyUpper: 0, uncertaintyLower: 0,
            regime: "", regimeConfidence: 0,
            continuationConviction: contConviction,
            reversalConviction: revConviction,
            activeStructures: flags,
            sweepReclaimScore, breakoutFailScore, exhaustionScore,
            absorptionScore, liqClusterScore,
            momentumPersistScore, cleanContScore, pullbackResumeScore,
            lowLiqRejectScore,
            consolidationScore, doubleStructScore, stopHuntScore,
            trendContScore, bosScore, chochScore, metaScore);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Utility ──────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Clamp value to [0, 1] range.</summary>
    public static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>Normalize raw feature value against config target → [0, 1].</summary>
    public static double Normalize(double rawValue, double normTarget)
        => normTarget > 0 ? Clamp01(rawValue / normTarget) : 0.0;

    /// <summary>Parse score from CepEvent context string (format: "score:0.72:...").</summary>
    private static double ParseScoreFromContext(string context)
    {
        if (string.IsNullOrEmpty(context)) return 0.5;
        int idx = context.IndexOf("score:");
        if (idx < 0) return 0.5;
        string numPart = context.Substring(idx + 6).Split(':')[0];
        if (double.TryParse(numPart,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score))
            return Clamp01(score);
        return 0.5;
    }

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
