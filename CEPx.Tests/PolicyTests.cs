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
        PolicyEngine.Reset();
    }

    // ── Structural Exit Tests ─────────────────────────────────────

    [Fact]
    public void Exit_fires_on_momentum_decay_sim_below_020()
    {
        ResetPosition();
        // Feed declining sim history (required: BOTH sim<0.20 AND declining)
        PolicyEngine.RecordPatternSimilarity(0.30);
        PolicyEngine.RecordPatternSimilarity(0.25);
        PolicyEngine.RecordPatternSimilarity(0.20);
        var state = MakeState(patternSimilarity: 0.15);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Need 4 consecutive ticks for momentum_decay hysteresis
        PolicyDecision result = default;
        for (int i = 0; i < 4; i++)
            result = PolicyEngine.Decide(state, currentTickIndex: 5 + i, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("momentum_decay", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_momentum_decay_declining_3_ticks()
    {
        ResetPosition();
        // Feed declining pattern similarities
        PolicyEngine.RecordPatternSimilarity(0.25);
        PolicyEngine.RecordPatternSimilarity(0.22);
        PolicyEngine.RecordPatternSimilarity(0.19);
        var state = MakeState(patternSimilarity: 0.16); // BOTH <0.20 AND declining
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Need 4 consecutive ticks for hysteresis
        PolicyDecision result = default;
        for (int i = 0; i < 4; i++)
            result = PolicyEngine.Decide(state, currentTickIndex: 5 + i, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("momentum_decay", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_structural_invalidation()
    {
        ResetPosition();
        var state = MakeState();
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Sweep origin was 42050, current price 42040 < origin -> invalidated
        var result = PolicyEngine.Decide(state, currentTickIndex: 5, currentPrice: 42040, sweepOriginPrice: 42050, isBullishSweep: true);
        Assert.Equal("exit", result.Action);
        Assert.Equal("structural_invalidation", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_trapped_order_flow()
    {
        ResetPosition();
        var state = MakeState(reversalScore: 0.4, anomalyScore: 0.4);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Within 5 ticks, high reversal + high anomaly -> trapped
        var result = PolicyEngine.Decide(state, currentTickIndex: 3, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("trapped_order_flow", result.Reason);
    }

    [Fact]
    public void Trapped_order_flow_does_not_fire_after_5_ticks()
    {
        ResetPosition();
        var state = MakeState(reversalScore: 0.4, anomalyScore: 0.4, patternSimilarity: 0.5);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Call Decide 6 times to push _ticksSinceEntry past 5
        PolicyDecision result = default;
        for (int i = 0; i < 6; i++)
            result = PolicyEngine.Decide(state, currentTickIndex: i, currentPrice: 42100);
        // The 6th call should NOT be trapped
        Assert.NotEqual("trapped_order_flow", result.Reason);
    }

    // ── Updated Mechanical Exit Tests ────────────────────────────

    [Fact]
    public void Exit_fires_on_time_stop()
    {
        ResetPosition();
        var state = MakeState(patternSimilarity: 0.5); // keep sim high to avoid momentum_decay
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // 40 ticks later -> time stop (was 20, now 40)
        var result = PolicyEngine.Decide(state, currentTickIndex: 40, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("time_stop", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_stop_loss()
    {
        ResetPosition();
        var state = MakeState(patternSimilarity: 0.5); // keep sim high
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // -1.2% PnL -> below STOP_LOSS_PCT (-1.0%)
        var result = PolicyEngine.Decide(state, currentTickIndex: 5, currentPrice: 41496);
        Assert.Equal("exit", result.Action);
        Assert.Equal("stop_loss", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_reversal_score()
    {
        ResetPosition();
        var state = MakeState(reversalScore: 0.6, patternSimilarity: 0.5);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        var result = PolicyEngine.Decide(state, currentTickIndex: 10, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("reversal_signal", result.Reason);
    }

    [Fact]
    public void Exit_fires_on_velocity_flip()
    {
        ResetPosition();
        var state = MakeState(kalmanVelocity: -1.0, patternSimilarity: 0.5);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "long";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        // Need 2 consecutive ticks for velocity_flip hysteresis
        PolicyDecision result = default;
        for (int i = 0; i < 2; i++)
            result = PolicyEngine.Decide(state, currentTickIndex: 10 + i, currentPrice: 42100);
        Assert.Equal("exit", result.Action);
        Assert.Equal("velocity_flip", result.Reason);

        // Short position + positive velocity = flip
        ResetPosition();
        var state2 = MakeState(kalmanVelocity: 1.0, patternSimilarity: 0.5);
        PolicyEngine.InPosition = true;
        PolicyEngine.PositionSide = "short";
        PolicyEngine.EntryPrice = 42000;
        PolicyEngine.EntryTick = 0;
        PolicyDecision result2 = default;
        for (int i = 0; i < 2; i++)
            result2 = PolicyEngine.Decide(state2, currentTickIndex: 10 + i, currentPrice: 41900);
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
