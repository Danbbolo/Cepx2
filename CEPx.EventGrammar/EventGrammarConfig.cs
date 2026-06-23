namespace CEPx.EventGrammar;

public class EventGrammarConfig
{
    public double SweepPctThreshold { get; set; } = 0.2;
    public int SweepWindowTicks { get; set; } = 5;
    public int ReclaimWindow { get; set; } = 10;
    public double MinVolumeMultiplier { get; set; } = 2.0;
    public int AbsorptionWindow { get; set; } = 5;
    public double AbsorptionVolumeMultiplier { get; set; } = 3.0;
    public double BreakoutPct { get; set; } = 0.2;
    public int BreakoutRangeWindow { get; set; } = 10;
    public double ExhaustionMovePct { get; set; } = 0.3;
    public double ExhaustionReversalRatio { get; set; } = 0.5;
}
