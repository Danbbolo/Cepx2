using CEPx.Core;

namespace CEPx.Scoring;

/// <summary>
/// Per-structure scoring functions for the market-structure scoring layer.
/// Each method takes a price window + optional event/detector output,
/// normalizes features against ScoringConfig targets, and returns [0, 1].
///
/// Phase 1: ALL METHODS ARE STUBS returning 0.0.
/// Phase 2–3: Implement one at a time using existing EventGrammar detector logic.
/// </summary>
public static class StructureScorers
{
    // ═══════════════════════════════════════════════════════════════
    // ── Reversal structures ──────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score sweep + reclaim: price crosses back past sweep origin.
    /// Uses existing ReclaimDetector output.
    /// </summary>
    public static double ScoreSweepReclaim(
        MarketEvent[] priceWindow,
        double sweepOrigin,
        bool isBullishSweep,
        CepEvent? reclaimEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
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
    /// Wraps existing ExhaustionDetector internal scoring logic.
    /// </summary>
    public static double ScoreExhaustion(
        MarketEvent[] priceWindow,
        CepEvent? exhaustionEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
    }

    /// <summary>
    /// Score absorption after aggressive move: high volume + minimal price change.
    /// Wraps existing AbsorptionAfterSweepDetector internal scoring logic.
    /// </summary>
    public static double ScoreAbsorption(
        MarketEvent[] priceWindow,
        CepEvent? absorptionEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
    }

    /// <summary>
    /// Score liquidation cluster at extreme: liquidations against sweep direction.
    /// Wraps existing LiquidationClusterDetector internal scoring logic.
    /// </summary>
    public static double ScoreLiquidationCluster(
        MarketEvent[] priceWindow,
        CepEvent? liqClusterEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Continuation structures ──────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score momentum persistence: sustained directional movement.
    /// Wraps existing MomentumPersistenceDetector internal scoring logic.
    /// </summary>
    public static double ScoreMomentumPersistence(
        MarketEvent[] priceWindow,
        CepEvent? momentumEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
    }

    /// <summary>
    /// Score clean continuation: directional move without opposing absorption.
    /// Wraps existing NoMeaningfulAbsorptionDetector internal scoring logic.
    /// </summary>
    public static double ScoreCleanContinuation(
        MarketEvent[] priceWindow,
        CepEvent? cleanContEvent,
        ScoringConfig cfg)
    {
        // STUB — Phase 2
        return 0.0;
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
}
