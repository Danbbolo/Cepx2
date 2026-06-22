using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    public static bool LiveMode;

    public static PolicyDecision Decide(BlackboardState state)
    {
        return Policy.PolicyEngine.Decide(state);
    }
}
