using System.Reactive.Linq;
using System.Reactive.Subjects;
using CEPx.Core;
using CEPx.EventGrammar;
using Xunit;

namespace CEPx.Tests;

public class ReclaimDetectorTests
{
    [Fact]
    public void Reclaim_fires_when_price_crosses_below_bullish_sweep_origin()
    {
        var config = new EventGrammarConfig { ReclaimWindow = 10 };
        var detector = new ReclaimDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        // Fire a bullish sweep at 42300
        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42300, "bullish"));

        // First tick drops below sweep origin → reclaim fires immediately
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42250, 1, 0, 0, 0));

        Assert.Single(results);
        Assert.Equal("Reclaim", results[0].Type);
        Assert.Equal(42250, results[0].Price);
    }

    [Fact]
    public void Reclaim_does_not_fire_if_price_stays_above_bullish_origin()
    {
        var config = new EventGrammarConfig { ReclaimWindow = 10 };
        var detector = new ReclaimDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42300, "bullish"));
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42400, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42500, 1, 0, 0, 0));

        Assert.Empty(results);
    }

    [Fact]
    public void Reclaim_fires_when_price_crosses_above_bearish_sweep_origin()
    {
        var config = new EventGrammarConfig { ReclaimWindow = 10 };
        var detector = new ReclaimDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bearish"));
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42050, 1, 0, 0, 0));

        Assert.Single(results);
        Assert.Equal("Reclaim", results[0].Type);
        Assert.Equal("bearish", results[0].Context);
    }

    [Fact]
    public void Reclaim_does_not_fire_after_window_expires()
    {
        var config = new EventGrammarConfig { ReclaimWindow = 2 }; // only 2 ticks
        var detector = new ReclaimDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42300, "bullish"));

        // First 2 ticks stay above origin (no reclaim)
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42400, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42500, 1, 0, 0, 0));
        // 3rd tick crosses below, but window expired
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42200, 1, 0, 0, 0));

        Assert.Empty(results);
    }
}
