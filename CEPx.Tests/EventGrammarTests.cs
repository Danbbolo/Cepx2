using CEPx.Core;
using CEPx.Pipeline;
using Xunit;

namespace CEPx.Tests;

public class EventGrammarTests
{
    [Fact]
    public void SweepStart_fires_on_valid_synthetic_data()
    {
        var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
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
    public void SweepStart_does_not_fire_on_high_move_low_volume()
    {
        var ticks = new MarketEvent[]
        {
            new(0,   "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
            new(100, "BTCUSDT", 42100.0, 1.0, 0, 0, 0),
            new(200, "BTCUSDT", 42200.0, 1.0, 0, 0, 0),
            new(300, "BTCUSDT", 42300.0, 1.0, 0, 0, 0),
            new(400, "BTCUSDT", 42420.0, 1.0, 0, 0, 0),
        };
        var result = PipelineFunctions.DetectSweepStart(ticks);
        Assert.Null(result);
    }

    [Fact]
    public void Dtw_near_zero_cost_on_identical_sequences()
    {
        double[] proto = { 1.0, 2.0, 3.0 };
        double[] cand  = { 1.0, 2.0, 3.0 };
        var d = PipelineFunctions.ComputeDtw(proto, cand, 1);
        Assert.True(d < 0.001);
    }

    [Fact]
    public void Dtw_returns_MaxValue_for_sequences_over_50()
    {
        double[] big = new double[51];
        var d = PipelineFunctions.ComputeDtw(big, big, 3);
        Assert.Equal(double.MaxValue, d);
    }

    [Fact]
    public void Kalman_bands_Upper_greater_StateMean_greater_Lower()
    {
        var evt = new CepEvent(0, "BTCUSDT", "SweepStart", 42300.0, "");
        var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
        var score = PipelineFunctions.ScoreWithKalman(evt, ticks);
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
        var score = PipelineFunctions.ScoreEvent(evt, ticks);
        Assert.Equal(0.0, score.PatternSimilarity);
    }

    [Fact]
    public void SyntheticTicks_first_timestamp_is_zero()
    {
        var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
        Assert.Equal(0L, ticks[0].Timestamp);
    }
}
