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
}
