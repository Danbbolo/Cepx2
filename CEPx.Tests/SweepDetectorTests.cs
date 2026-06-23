using System.Reactive.Linq;
using CEPx.Core;
using CEPx.EventGrammar;
using Microsoft.Reactive.Testing;
using Xunit;

namespace CEPx.Tests;

public class SweepDetectorTests
{
    [Fact]
    public void SweepStart_fires_on_clean_sweep()
    {
        var scheduler = new TestScheduler();
        var config = new EventGrammarConfig { SweepPctThreshold = 0.2, SweepWindowTicks = 5 };
        var detector = new SweepDetector(config);

        var ticks = new[]
        {
            new MarketEvent(0,    "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(100,  "BTCUSDT", 42030, 1, 0, 0, 0),
            new MarketEvent(200,  "BTCUSDT", 42080, 1, 0, 0, 0),
            new MarketEvent(300,  "BTCUSDT", 42150, 1, 0, 0, 0),
            new MarketEvent(400,  "BTCUSDT", 42300, 1, 0, 0, 0),
        };

        var source = ticks.ToObservable();
        var results = new List<CepEvent>();
        detector.Detect(source).Subscribe(e => results.Add(e));

        Assert.Single(results);
        Assert.Equal("SweepStart", results[0].Type);
        Assert.Equal("bullish", results[0].Context);
    }

    [Fact]
    public void SweepStart_does_not_fire_on_flat_price()
    {
        var config = new EventGrammarConfig { SweepPctThreshold = 0.2, SweepWindowTicks = 5 };
        var detector = new SweepDetector(config);

        var ticks = new[]
        {
            new MarketEvent(0,   "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(100, "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(200, "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(300, "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(400, "BTCUSDT", 42000, 1, 0, 0, 0),
        };

        var source = ticks.ToObservable();
        var results = new List<CepEvent>();
        detector.Detect(source).Subscribe(e => results.Add(e));

        Assert.Empty(results);
    }

    [Fact]
    public void SweepStart_does_not_fire_below_threshold()
    {
        var config = new EventGrammarConfig { SweepPctThreshold = 1.0, SweepWindowTicks = 5 }; // high threshold
        var detector = new SweepDetector(config);

        var ticks = new[]
        {
            new MarketEvent(0,   "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(100, "BTCUSDT", 42030, 1, 0, 0, 0),
            new MarketEvent(200, "BTCUSDT", 42080, 1, 0, 0, 0),
            new MarketEvent(300, "BTCUSDT", 42150, 1, 0, 0, 0),
            new MarketEvent(400, "BTCUSDT", 42300, 1, 0, 0, 0),
        };

        var source = ticks.ToObservable();
        var results = new List<CepEvent>();
        detector.Detect(source).Subscribe(e => results.Add(e));

        Assert.Empty(results); // range is ~0.71%, below 1.0%
    }

    [Fact]
    public void SweepStart_detects_bearish_sweep()
    {
        var config = new EventGrammarConfig { SweepPctThreshold = 0.2, SweepWindowTicks = 5 };
        var detector = new SweepDetector(config);

        var ticks = new[]
        {
            new MarketEvent(0,   "BTCUSDT", 42300, 1, 0, 0, 0),
            new MarketEvent(100, "BTCUSDT", 42200, 1, 0, 0, 0),
            new MarketEvent(200, "BTCUSDT", 42100, 1, 0, 0, 0),
            new MarketEvent(300, "BTCUSDT", 42050, 1, 0, 0, 0),
            new MarketEvent(400, "BTCUSDT", 42000, 1, 0, 0, 0),
        };

        var source = ticks.ToObservable();
        var results = new List<CepEvent>();
        detector.Detect(source).Subscribe(e => results.Add(e));

        Assert.Single(results);
        Assert.Equal("bearish", results[0].Context);
    }

    [Fact]
    public void SweepStart_respects_config_window_size()
    {
        var config = new EventGrammarConfig { SweepPctThreshold = 0.2, SweepWindowTicks = 3 };
        var detector = new SweepDetector(config);

        var ticks = new[]
        {
            new MarketEvent(0,   "BTCUSDT", 42000, 1, 0, 0, 0),
            new MarketEvent(100, "BTCUSDT", 42100, 1, 0, 0, 0),
            new MarketEvent(200, "BTCUSDT", 42200, 1, 0, 0, 0),
        };

        var source = ticks.ToObservable();
        var results = new List<CepEvent>();
        detector.Detect(source).Subscribe(e => results.Add(e));

        Assert.Single(results);
        Assert.Equal("SweepStart", results[0].Type);
    }
}
