namespace CEPx.Core;

public readonly struct BlackboardState
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly bool SweepActive;
    public readonly string PatternFamily;
    public readonly double PatternSimilarity;
    public readonly double KalmanVelocity;
    public readonly double UncertaintyUpper;
    public readonly double UncertaintyLower;
    public readonly double AnomalyScore;
    public readonly string Regime;
    public readonly double RegimeConfidence;
    public readonly string LastAction;
}