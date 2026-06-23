namespace CEPx.Scoring;

/// <summary>
/// Normalization targets and weights for the market-structure scoring layer.
/// All features are normalized to [0, 1] via: Clamp(raw / normTarget, 0, 1).
///
/// Phase 1: Placeholder defaults. Tunable after Phase 2–3 wiring.
/// Parallel to EventGrammarConfig (detection thresholds), this controls
/// how raw structure features are translated to conviction scores.
/// </summary>
public class ScoringConfig
{
    // ═══════════════════════════════════════════════════════════════
    // ── Conviction aggregation weights ───────────────────────────
    // ═══════════════════════════════════════════════════════════════

    // ── Continuation structure weights ──
    public double MomentumPersistenceWeight { get; set; } = 0.35;
    public double CleanContinuationWeight { get; set; } = 0.25;
    public double PullbackResumeWeight { get; set; } = 0.15;

    // ── Reversal structure weights ──
    public double SweepReclaimWeight { get; set; } = 0.25;
    public double ExhaustionWeight { get; set; } = 0.20;
    public double AbsorptionWeight { get; set; } = 0.20;
    public double LiqClusterWeight { get; set; } = 0.15;
    public double BreakoutFailWeight { get; set; } = 0.10;

    // ── Phase C: New structure weights ───────────────────────────
    public double ConsolidationWeight { get; set; } = 0.10;
    public double DoubleStructureWeight { get; set; } = 0.15;
    public double StopHuntWeight { get; set; } = 0.20;
    public double TrendContinuationWeight { get; set; } = 0.20;
    public double BOSWeight { get; set; } = 0.15;
    public double CHoCHWeight { get; set; } = 0.25;
    public double MetaWeight { get; set; } = 0.15;

    /// <summary>Bonus added to ReversalConviction when ≥ 2 reversal structures are active.</summary>
    public double ComboBonus { get; set; } = 0.15;

    // ═══════════════════════════════════════════════════════════════
    // ── Price structure normalization targets ────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Reclaim depth as % of sweep range → score 1.0.</summary>
    public double ReclaimDepthNormPct { get; set; } = 0.3;

    /// <summary>Retracement depth (30-50% = healthy). Above this → score decays.</summary>
    public double RetracementHealthyMaxPct { get; set; } = 0.5;

    /// <summary>Penetration depth for breakout-fail → score 1.0.</summary>
    public double PenetrationDepthNormPct { get; set; } = 0.2;

    /// <summary>Wick-to-body ratio → score 1.0 for rejection.</summary>
    public double WickBodyRatioNorm { get; set; } = 3.0;

    // ═══════════════════════════════════════════════════════════════
    // ── Volatility / Momentum normalization targets ──────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>|Kalman velocity| = this → score 1.0.</summary>
    public double KalmanVelocityNorm { get; set; } = 25.0;

    /// <summary>Deceleration ratio (second/first half range) ≤ this → score 1.0.</summary>
    public double DecelerationNorm { get; set; } = 0.15;

    /// <summary>Initial move magnitude % → score 1.0.</summary>
    public double InitialMoveNormPct { get; set; } = 0.5;

    /// <summary>Net move magnitude % → score 1.0.</summary>
    public double NetMoveNormPct { get; set; } = 0.5;

    /// <summary>Direction consistency (fraction of ticks in dominant dir) → score 1.0 at this value.</summary>
    public double DirectionConsistencyNorm { get; set; } = 0.85;

    // ═══════════════════════════════════════════════════════════════
    // ── Volume / Liquidity normalization targets ─────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Volume ratio (max / avg) → score 1.0.</summary>
    public double VolIntensityNorm { get; set; } = 8.0;

    /// <summary>Volume below this fraction of daily avg → "thin" regime.</summary>
    public double ThinVolumeRatio { get; set; } = 0.5;

    /// <summary>Resume volume ratio → score 1.0.</summary>
    public double ResumeVolumeNorm { get; set; } = 3.0;

    // ═══════════════════════════════════════════════════════════════
    // ── Order flow normalization targets (CHD only) ──────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Trade imbalance (net buy / total) → score 1.0. Unused without CHD.</summary>
    public double TradeImbalanceNorm { get; set; } = 0.3;

    /// <summary>Cumulative delta (absolute) → score 1.0. Unused without CHD.</summary>
    public double CumulativeDeltaNorm { get; set; } = 100.0;

    /// <summary>BidSize/AskSize ratio deviation from 1.0 → score 1.0. Unused without CHD.</summary>
    public double BidAskRatioNorm { get; set; } = 0.3;
}
