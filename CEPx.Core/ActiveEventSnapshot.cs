namespace CEPx.Core;

/// <summary>
/// Snapshot of currently active event-grammar signals at a point in time.
/// Used by ScoreMarket() to determine which structure scorers to call.
///
/// All CepEvent fields are nullable — null means the structure is not active.
/// TTL tracking is managed by PolicyEngine; this is a read-only snapshot.
/// </summary>
public readonly struct ActiveEventSnapshot
{
    /// <summary>Sweep origin price at the time of sweep detection.</summary>
    public readonly double SweepOrigin;

    /// <summary>Direction of the original sweep.</summary>
    public readonly bool IsBullishSweep;

    /// <summary>Kalman velocity from the most recent Kalman filter run.</summary>
    public readonly double KalmanVelocity;

    /// <summary>Daily average volume for thin-liquidity detection. 0 = unavailable.</summary>
    public readonly double DailyAvgVolume;

    // ── Reversal event signals ──
    public readonly CepEvent? Reclaim;
    public readonly CepEvent? Exhaustion;
    public readonly CepEvent? Absorption;
    public readonly CepEvent? LiquidationCluster;

    // ── Continuation event signals ──
    public readonly CepEvent? MomentumPersistence;
    public readonly CepEvent? CleanContinuation;

    public ActiveEventSnapshot(
        double sweepOrigin,
        bool isBullishSweep,
        double kalmanVelocity,
        double dailyAvgVolume,
        CepEvent? reclaim = null,
        CepEvent? exhaustion = null,
        CepEvent? absorption = null,
        CepEvent? liquidationCluster = null,
        CepEvent? momentumPersistence = null,
        CepEvent? cleanContinuation = null)
    {
        SweepOrigin = sweepOrigin;
        IsBullishSweep = isBullishSweep;
        KalmanVelocity = kalmanVelocity;
        DailyAvgVolume = dailyAvgVolume;
        Reclaim = reclaim;
        Exhaustion = exhaustion;
        Absorption = absorption;
        LiquidationCluster = liquidationCluster;
        MomentumPersistence = momentumPersistence;
        CleanContinuation = cleanContinuation;
    }
}
