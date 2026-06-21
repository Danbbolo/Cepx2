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

    public BlackboardState(long timestamp, string symbol, bool sweepActive, string patternFamily, double patternSimilarity, double kalmanVelocity, double uncertaintyUpper, double uncertaintyLower, double anomalyScore, string regime, double regimeConfidence, string lastAction)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        SweepActive = sweepActive;
        PatternFamily = patternFamily;
        PatternSimilarity = patternSimilarity;
        KalmanVelocity = kalmanVelocity;
        UncertaintyUpper = uncertaintyUpper;
        UncertaintyLower = uncertaintyLower;
        AnomalyScore = anomalyScore;
        Regime = regime;
        RegimeConfidence = regimeConfidence;
        LastAction = lastAction;
    }
}