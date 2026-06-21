using CEPx.Core;

namespace CEPx.Pipeline;

public static class PipelineFunctions
{
    public static CepEvent? IngestTick(MarketEvent tick)
    {
        return new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "");
    }

    public static StructuralScore ScoreEvent(CepEvent evt, MarketEvent[] window)
    {
        return new StructuralScore(0L, "", 0.0, 0.0, 0.0, 0.0, "", 0.0, 0.0);
    }

    public static BlackboardState WriteState(StructuralScore score)
    {
        return new BlackboardState(0L, "", false, "", 0.0, 0.0, 0.0, 0.0, 0.0, "", 0.0, "");
    }

    public static PolicyDecision Decide(BlackboardState state)
    {
        return new PolicyDecision(0L, "BTCUSDT", "noop", "", "", 0.0);
    }

    public static CepEvent[] ReplayTicks(MarketEvent[] ticks)
    {
        return new[] { new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "") };
    }
}