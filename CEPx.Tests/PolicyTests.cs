using CEPx.Core;
using CEPx.Policy;
using Xunit;

namespace CEPx.Tests;

public class PolicyTests
{
    private static BlackboardState MakeState(double reversalScore = 0.1, double patternSimilarity = 0.5, double kalmanVelocity = 1.0, bool sweepActive = true, string patternFamily = "sweep", double anomalyScore = 0.1)
    {
        return new BlackboardState(0, "BTCUSDT", sweepActive, patternFamily, patternSimilarity, reversalScore, kalmanVelocity, 0, 0, anomalyScore, "chop", 0.5, "hold");
    }

    private void ResetPosition()
    {
        PolicyEngine.InPosition = false;
        PolicyEngine.PositionSide = "";
        PolicyEngine.EntryPrice = 0;
        PolicyEngine.EntryTick = 0;
    }

    [Fact]
    public void Exit_fires_on_time_stop()
    {
        ResetPosition();
        var state = MakeState();
        // Enter first
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // 20 ticks later -> time stop
        var result = PolicyEngine.Decide(state, currentTickIndex: 20, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("time_stop", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_stop_loss()
    {
        ResetPosition();
        var state = MakeState();
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // -1% PnL -> below STOP_LOSS_PCT (-0.5%)
        var result = PolicyEngine.Decide(state, currentTickIndex: 5, currentPrice: 41580); // -1%
        Assert.Equal("exit", result.Action);
        Assert.Equal("stop_loss", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_reversal_score()
    {
        ResetPosition();
        var state = MakeState(reversalScore: 0.6);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        var result = PolicyEngine.Decide(state, currentTickIndex: 5, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("reversal_signal", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_velocity_flip()
    {
        ResetPosition();
        // Long position + negative velocity = flip
        var state = MakeState(kalmanVelocity: -1.0);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        var result = PolicyEngine.Decide(state, currentTickIndex: 5, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("velocity_flip", result.Reason);

        // Short position + positive velocity = flip
        ResetPosition();
        var state2 = MakeState(kalmanVelocity: 1.0);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "short";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        var result2 = PolicyEngine.Decide(state2, currentTickIndex: 5, currentPrice: 41900);
        Assert.Equal("exit", result2.Action);
        Assert.Equal("velocity_flip", result2.Reason);
    }

    [Fact]
    public void Exit_does_not_fire_when_no_position()
    {
        ResetPosition();
        var state = MakeState(reversalScore: 0.9, kalmanVelocity: -5.0);
        var result = PolicyEngine.Decide(state, currentTickIndex: 30, currentPrice: 41000);
        Assert.NotEqual("exit", result.Action);
    }
}
