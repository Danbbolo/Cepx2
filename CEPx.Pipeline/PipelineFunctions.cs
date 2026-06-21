using CEPx.Core;
using System.Reflection;

namespace CEPx.Pipeline;

public static class PipelineFunctions
{
    private static T CreateFake<T>(object data) where T : struct
    {
        object boxed = default(T);
        foreach (var prop in data.GetType().GetProperties())
            typeof(T).GetField(prop.Name)?.SetValue(boxed, prop.GetValue(data));
        return (T)boxed;
    }

    public static CepEvent? IngestTick(MarketEvent tick)
    {
        return CreateFake<CepEvent>(new { Timestamp = 0L, Symbol = "BTCUSDT", Type = "SweepStart", Price = 0.0, Context = "" });
    }

    public static StructuralScore ScoreEvent(CepEvent evt, MarketEvent[] window)
    {
        return default;
    }

    public static BlackboardState WriteState(StructuralScore score)
    {
        return default;
    }

    public static PolicyDecision Decide(BlackboardState state)
    {
        return CreateFake<PolicyDecision>(new { Timestamp = 0L, Symbol = "BTCUSDT", Action = "noop", Side = "", Reason = "", Quantity = 0.0 });
    }

    public static CepEvent[] ReplayTicks(MarketEvent[] ticks)
    {
        return new[] { CreateFake<CepEvent>(new { Timestamp = 0L, Symbol = "BTCUSDT", Type = "SweepStart", Price = 0.0, Context = "" }) };
    }
}