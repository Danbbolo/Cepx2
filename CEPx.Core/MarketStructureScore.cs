namespace CEPx.Core;

/// <summary>
/// Bitmask enum for active market structures detected in a scoring window.
/// Multiple structures can fire simultaneously.
/// </summary>
[Flags]
public enum StructureFlags
{
    None              = 0,

    // ── Reversal structures ──
    SweepReclaim      = 1 << 0,
    BreakoutFail      = 1 << 1,
    Exhaustion        = 1 << 2,
    Absorption        = 1 << 3,
    LiquidationCluster = 1 << 4,

    // ── Continuation structures ──
    MomentumPersistence = 1 << 5,
    CleanContinuation   = 1 << 6,
    PullbackResume      = 1 << 7,

    // ── Direction-agnostic ──
    LowLiquidityReject  = 1 << 8,

    // ── Phase B/C: New structures ──
    ConsolidationActive  = 1 << 9,
    DoubleStructure      = 1 << 10,
    StopHunt             = 1 << 11,
    TrendContinuation    = 1 << 12,
    BullishBOSFlag       = 1 << 13,
    BearishBOSFlag       = 1 << 14,
    BullishCHoCHFlag     = 1 << 15,
    BearishCHoCHFlag     = 1 << 16,
}

/// <summary>
/// Market-structure scoring output. Replaces the DTW prototype-based
/// StructuralScore once the new scoring layer is fully integrated.
///
/// Phase 1: Created alongside old StructuralScore. Policy code still reads
/// BlackboardState (unchanged). This type is unused until Phase 4 wiring.
/// </summary>
public readonly struct MarketStructureScore
{
    // ── Kalman state (unchanged from StructuralScore) ──
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly double StateMean;
    public readonly double StateVelocity;
    public readonly double UncertaintyUpper;
    public readonly double UncertaintyLower;

    // ── Regime (unchanged) ──
    public readonly string Regime;
    public readonly double RegimeConfidence;

    // ── Directional conviction (new) ──
    /// <summary>0.0–1.0: strength of the continuation (trend-following) case.</summary>
    public readonly double ContinuationConviction;

    /// <summary>0.0–1.0: strength of the reversal (fade-the-sweep) case.</summary>
    public readonly double ReversalConviction;

    // ── Active structures bitmask ──
    public readonly StructureFlags ActiveStructures;

    // ── Individual structure scores (for diagnostics) ──
    public readonly double SweepReclaimScore;
    public readonly double BreakoutFailScore;
    public readonly double ExhaustionScore;
    public readonly double AbsorptionScore;
    public readonly double LiqClusterScore;
    public readonly double MomentumPersistScore;
    public readonly double CleanContScore;
    public readonly double PullbackResumeScore;
    public readonly double LowLiquidityRejectScore;

    // ── Phase B/C: New structure scores ──
    public readonly double ConsolidationScore;
    public readonly double DoubleStructureScore;
    public readonly double StopHuntScore;
    public readonly double TrendContinuationScore;
    public readonly double BOSScore;
    public readonly double CHoCHScore;
    public readonly double MetaScore;

    // ── Legacy compat: same names as StructuralScore fields ──
    /// <summary>Legacy compat: maps to ContinuationConviction.</summary>
    public readonly double PatternSimilarity => ContinuationConviction;

    /// <summary>Legacy compat: maps to ReversalConviction.</summary>
    public readonly double ReversalSimilarity => ReversalConviction;

    /// <summary>Legacy compat: 1.0 - max(cont, rev) as anomaly score.</summary>
    public readonly double AnomalyScore => 1.0 - Math.Max(ContinuationConviction, ReversalConviction);

    /// <summary>Legacy compat: "structure" if any flag set, else "sweep".</summary>
    public readonly string PatternFamily => ActiveStructures != StructureFlags.None ? "structure" : "sweep";

    public MarketStructureScore(
        long timestamp,
        string symbol,
        double stateMean,
        double stateVelocity,
        double uncertaintyUpper,
        double uncertaintyLower,
        string regime,
        double regimeConfidence,
        double continuationConviction,
        double reversalConviction,
        StructureFlags activeStructures,
        double sweepReclaimScore = 0,
        double breakoutFailScore = 0,
        double exhaustionScore = 0,
        double absorptionScore = 0,
        double liqClusterScore = 0,
        double momentumPersistScore = 0,
        double cleanContScore = 0,
        double pullbackResumeScore = 0,
        double lowLiquidityRejectScore = 0,
        double consolidationScore = 0,
        double doubleStructureScore = 0,
        double stopHuntScore = 0,
        double trendContinuationScore = 0,
        double bosScore = 0,
        double chochScore = 0,
        double metaScore = 0)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        StateMean = stateMean;
        StateVelocity = stateVelocity;
        UncertaintyUpper = uncertaintyUpper;
        UncertaintyLower = uncertaintyLower;
        Regime = regime;
        RegimeConfidence = regimeConfidence;
        ContinuationConviction = continuationConviction;
        ReversalConviction = reversalConviction;
        ActiveStructures = activeStructures;
        SweepReclaimScore = sweepReclaimScore;
        BreakoutFailScore = breakoutFailScore;
        ExhaustionScore = exhaustionScore;
        AbsorptionScore = absorptionScore;
        LiqClusterScore = liqClusterScore;
        MomentumPersistScore = momentumPersistScore;
        CleanContScore = cleanContScore;
        PullbackResumeScore = pullbackResumeScore;
        LowLiquidityRejectScore = lowLiquidityRejectScore;
        ConsolidationScore = consolidationScore;
        DoubleStructureScore = doubleStructureScore;
        StopHuntScore = stopHuntScore;
        TrendContinuationScore = trendContinuationScore;
        BOSScore = bosScore;
        CHoCHScore = chochScore;
        MetaScore = metaScore;
    }
}
