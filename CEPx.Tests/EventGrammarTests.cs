using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Scoring;
using Xunit;

namespace CEPx.Tests;

public class EventGrammarTests
{
    [Fact]
    public void SweepStart_fires_on_valid_synthetic_data()
    {
        var ticks = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42030.0, 1.2, 0, 0, 0),
            new(200, "BTCUSDT", 42080.0, 1.5, 0, 0, 0),
            new(300, "BTCUSDT", 42150.0, 2.1, 0, 0, 0),
            new(400, "BTCUSDT", 42300.0, 5.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectSweepStart(ticks);
        Assert.NotNull(result);
        Assert.Equal("SweepStart", result.Value.Type);
    }

    [Fact]
    public void SweepStart_does_not_fire_on_flat_price()
    {
        var ticks = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectSweepStart(ticks);
        Assert.Null(result);
    }

    [Fact]
    public void SweepStart_fires_on_high_move_even_with_low_volume()
    {
        // Volume filter removed � detector is pure. BT filters later.
        var ticks = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42300.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42420.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectSweepStart(ticks);
        Assert.NotNull(result);
        Assert.Equal("SweepStart", result.Value.Type);
    }

    [Fact]
    public void Dtw_near_zero_cost_on_identical_sequences()
    {
        double[] proto = { 1.0, 2.0, 3.0 };
        double[] cand  = { 1.0, 2.0, 3.0 };
        var d = ScoringEngine.ComputeDtw(proto, cand, 1);
        Assert.True(d < 0.001);
    }

    [Fact]
    public void Dtw_returns_MaxValue_for_sequences_over_50()
    {
        double[] big = new double[51];
        var d = ScoringEngine.ComputeDtw(big, big, 3);
        Assert.Equal(double.MaxValue, d);
    }

    [Fact]
    public void Kalman_bands_Upper_greater_StateMean_greater_Lower()
    {
        var evt = new CepEvent(0, "BTCUSDT", "SweepStart", 42300.0, "");
        var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
        var score = ScoringEngine.ScoreWithKalman(evt, ticks);
        Assert.True(score.UncertaintyUpper > score.StateMean);
        Assert.True(score.StateMean > score.UncertaintyLower);
    }

    [Fact]
    public void Flat_sequence_returns_PatternSimilarity_zero()
    {
        var evt = new CepEvent(0, "BTCUSDT", "SweepStart", 42000.0, "");
        var ticks = new MarketEvent[10];
        for (int i = 0; i < 10; i++)
            ticks[i] = new MarketEvent(i * 100L, "BTCUSDT", 42000.0, 1.0, 0, 0, 0);
        var score = ScoringEngine.ScoreEvent(evt, ticks);
        Assert.Equal(0.0, score.PatternSimilarity);
    }

    [Fact]
    public void SyntheticTicks_first_timestamp_is_zero()
    {
        var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
        Assert.Equal(0L, ticks[0].Timestamp);
    }

    [Fact]
    public void Reclaim_fires_when_bullish_sweep_price_is_reclaimed()
    {
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42350.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectReclaim(window, 42300.0, true);
        Assert.NotNull(result);
        Assert.Equal("Reclaim", result.Value.Type);
    }

    [Fact]
    public void Reclaim_does_not_fire_when_price_has_not_reclaimed()
    {
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42500.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42450.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42400.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectReclaim(window, 42300.0, true);
        Assert.Null(result);
    }

    [Fact]
    public void Absorption_fires_on_volume_spike_with_no_price_move()
    {
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42000.0, 4.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectAbsorption(window);
        Assert.NotNull(result);
        Assert.Equal("AbsorptionAfterSweep", result.Value.Type);
    }

    [Fact]
    public void Absorption_does_not_fire_when_price_moves_with_volume()
    {
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42100.0, 4.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectAbsorption(window);
        Assert.Null(result);
    }

    [Fact]
    public void BreakoutAttempt_fires_on_bullish_breakout()
    {
        var window = new MarketEvent[11];
        for (int i = 0; i < 10; i++)
            window[i] = new MarketEvent(i * 100L, "BTCUSDT", 42000.0 + i * 10, 1.0, 0, 0, 0);
        window[10] = new MarketEvent(1000, "BTCUSDT", 42300.0, 1.0, 0, 0, 0);
        var result = PipelineFunctions.DetectBreakoutAttempt(window);
        Assert.NotNull(result);
        Assert.Equal("BreakoutAttempt", result.Value.Type);
        Assert.Equal("bullish", result.Value.Context);
    }

    [Fact]
    public void BreakoutAttempt_does_not_fire_inside_range()
    {
        var window = new MarketEvent[11];
        for (int i = 0; i < 10; i++)
            window[i] = new MarketEvent(i * 100L, "BTCUSDT", 42000.0 + i * 10, 1.0, 0, 0, 0);
        window[10] = new MarketEvent(1000, "BTCUSDT", 42050.0, 1.0, 0, 0, 0);
        var result = PipelineFunctions.DetectBreakoutAttempt(window);
        Assert.Null(result);
    }

    [Fact]
    public void ExhaustionPulse_fires_on_strong_reversal()
    {
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
            new(500, "BTCUSDT", 41900.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectExhaustionPulse(window);
        Assert.NotNull(result);
        Assert.Equal("ExhaustionPulse", result.Value.Type);
        Assert.StartsWith("reversal_exhaustion", result.Value.Context);
    }

    [Fact]
    public void ExhaustionPulse_does_not_fire_on_weak_reversal()
    {
        // All ticks move consistently — no exhaustion pattern anywhere
        var window = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42050.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42150.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(500, "BTCUSDT", 42250.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectExhaustionPulse(window);
        Assert.Null(result);
    }
}
