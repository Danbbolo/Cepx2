namespace CEPx.Core;

public readonly struct StructuralScore
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly double StateMean;
    public readonly double StateVelocity;
    public readonly double UncertaintyUpper;
    public readonly double UncertaintyLower;
    public readonly string PatternFamily;
    public readonly double PatternSimilarity;
    public readonly double AnomalyScore;
}