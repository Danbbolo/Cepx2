namespace CEPx.Core;

/// <summary>
/// Snapshot of currently active event-grammar signals and market-structure context
/// at a point in time. Used by ScoreMarket() to determine which structure scorers to call.
///
/// All CepEvent fields are nullable — null means the structure is not active.
/// TTL tracking is managed by PolicyEngine; swing/volume state comes from foundation trackers.
/// </summary>
public readonly struct ActiveEventSnapshot
{
    // ── Sweep context ────────────────────────────────────────────
    public readonly double SweepOrigin;
    public readonly bool IsBullishSweep;
    public readonly double KalmanVelocity;

    // ── Reversal event signals ──
    public readonly CepEvent? Reclaim;
    public readonly CepEvent? Exhaustion;
    public readonly CepEvent? Absorption;
    public readonly CepEvent? LiquidationCluster;

    // ── Continuation event signals ──
    public readonly CepEvent? MomentumPersistence;
    public readonly CepEvent? CleanContinuation;

    // ── Phase B/C: New structure events ──────────────────────────
    public readonly CepEvent? Consolidation;
    public readonly CepEvent? DoubleStructure;   // "DoubleTop" or "DoubleBottom"
    public readonly CepEvent? StopHunt;

    // ── Swing state (from SwingTracker) ──────────────────────────
    public readonly double SwingHigh;
    public readonly double SwingLow;
    public readonly double CurrentSwingRange;
    public readonly int LastSwingDirection;
    public readonly bool BullishBOS;
    public readonly bool BearishBOS;
    public readonly double BOSPrice;
    public readonly long BOSTimestamp;
    public readonly bool BullishCHoCH;
    public readonly bool BearishCHoCH;
    public readonly long CHoCHTimestamp;

    // ── Volume context (from VolumeContextTracker) ────────────────
    public readonly double DailyAvgVolume;
    public readonly double RecentAvgVolume;
    public readonly bool IsVolumeExpanding;
    public readonly bool IsThinVolume;
    public readonly double VolumeRatio;

    // ═══════════════════════════════════════════════════════════════
    // ── Constructors ──────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Full constructor with all fields. Used by PolicyEngine.SnapshotActiveEvents().
    /// </summary>
    public ActiveEventSnapshot(
        double sweepOrigin, bool isBullishSweep, double kalmanVelocity,
        double dailyAvgVolume, double recentAvgVolume,
        bool isVolumeExpanding, bool isThinVolume, double volumeRatio,
        double swingHigh, double swingLow, double currentSwingRange, int lastSwingDirection,
        bool bullishBOS, bool bearishBOS, double bosPrice, long bosTimestamp,
        bool bullishCHoCH, bool bearishCHoCH, long chochTimestamp,
        CepEvent? reclaim = null,
        CepEvent? exhaustion = null,
        CepEvent? absorption = null,
        CepEvent? liquidationCluster = null,
        CepEvent? momentumPersistence = null,
        CepEvent? cleanContinuation = null,
        CepEvent? consolidation = null,
        CepEvent? doubleStructure = null,
        CepEvent? stopHunt = null)
    {
        SweepOrigin = sweepOrigin;
        IsBullishSweep = isBullishSweep;
        KalmanVelocity = kalmanVelocity;
        DailyAvgVolume = dailyAvgVolume;
        RecentAvgVolume = recentAvgVolume;
        IsVolumeExpanding = isVolumeExpanding;
        IsThinVolume = isThinVolume;
        VolumeRatio = volumeRatio;
        SwingHigh = swingHigh;
        SwingLow = swingLow;
        CurrentSwingRange = currentSwingRange;
        LastSwingDirection = lastSwingDirection;
        BullishBOS = bullishBOS;
        BearishBOS = bearishBOS;
        BOSPrice = bosPrice;
        BOSTimestamp = bosTimestamp;
        BullishCHoCH = bullishCHoCH;
        BearishCHoCH = bearishCHoCH;
        CHoCHTimestamp = chochTimestamp;
        Reclaim = reclaim;
        Exhaustion = exhaustion;
        Absorption = absorption;
        LiquidationCluster = liquidationCluster;
        MomentumPersistence = momentumPersistence;
        CleanContinuation = cleanContinuation;
        Consolidation = consolidation;
        DoubleStructure = doubleStructure;
        StopHunt = stopHunt;
    }

    /// <summary>
    /// Minimal constructor for backward compat (exit checks, initial sweep).
    /// Swing/volume fields default to 0/false.
    /// </summary>
    public ActiveEventSnapshot(
        double sweepOrigin, bool isBullishSweep, double kalmanVelocity,
        double dailyAvgVolume,
        CepEvent? reclaim = null,
        CepEvent? exhaustion = null,
        CepEvent? absorption = null,
        CepEvent? liquidationCluster = null,
        CepEvent? momentumPersistence = null,
        CepEvent? cleanContinuation = null)
        : this(sweepOrigin, isBullishSweep, kalmanVelocity,
               dailyAvgVolume, 0, false, false, 1.0,
               0, 0, 0, 0, false, false, 0, 0, false, false, 0,
               reclaim, exhaustion, absorption, liquidationCluster,
               momentumPersistence, cleanContinuation,
               null, null, null)
    { }
}
