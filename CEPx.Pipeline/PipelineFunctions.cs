using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    public static bool LiveMode;

    public static CepEvent? IngestTick(MarketEvent tick)
    {
        return new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "");
    }

    public static BlackboardState WriteState(StructuralScore score, MarketEvent[] window)
    {
        return new BlackboardState(0L, "", false, "", 0.0, 0.0, 0.0, 0.0, 0.0, "", 0.0, "");
    }

    public static PolicyDecision Decide(BlackboardState state)
    {
        return Policy.PolicyEngine.Decide(state);
    }
}
