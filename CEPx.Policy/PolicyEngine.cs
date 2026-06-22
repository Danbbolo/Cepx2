using CEPx.Core;

namespace CEPx.Policy;

public static class PolicyEngine
{
    private const double SIMILARITY_THRESHOLD = 0.5;
    private const double ANOMALY_THRESHOLD = 0.5;

    public static PolicyDecision Decide(BlackboardState state)
    {
        if (state.SweepActive
            && state.PatternFamily == "sweep"
            && state.PatternSimilarity > SIMILARITY_THRESHOLD
            && state.AnomalyScore < ANOMALY_THRESHOLD)
            return new PolicyDecision(state.Timestamp, state.Symbol, "enter", "long", "sweep_confirmed", 1.0);

        return new PolicyDecision(state.Timestamp, state.Symbol, "noop", "", "", 0.0);
    }
}
