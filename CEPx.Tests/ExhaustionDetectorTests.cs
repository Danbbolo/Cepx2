using System.Reactive.Subjects;
using CEPx.Core;
using CEPx.EventGrammar;
using Xunit;

namespace CEPx.Tests;

public class ExhaustionDetectorTests
{
    [Fact]
    public void Exhaustion_fires_when_momentum_decelerates()
    {
        var config = new EventGrammarConfig { ExhaustionWindow = 6, ExhaustionStallRatio = 0.3 };
        var detector = new ExhaustionDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        // First 3 ticks: strong continuation (wide range)
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42100, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42200, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42300, 1, 0, 0, 0));
        // Next 3 ticks: stalling (tiny range)
        tickStream.OnNext(new MarketEvent(400, "BTCUSDT", 42310, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(500, "BTCUSDT", 42305, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(600, "BTCUSDT", 42315, 1, 0, 0, 0));

        Assert.Single(results);
        Assert.Equal("ExhaustionAfterSweep", results[0].Type);
    }

    [Fact]
    public void Exhaustion_does_not_fire_if_momentum_continues()
    {
        var config = new EventGrammarConfig { ExhaustionWindow = 6, ExhaustionStallRatio = 0.3 };
        var detector = new ExhaustionDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        // Consistent strong moves throughout
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42100, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42200, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42300, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(400, "BTCUSDT", 42400, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(500, "BTCUSDT", 42500, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(600, "BTCUSDT", 42600, 1, 0, 0, 0));

        Assert.Empty(results);
    }

    [Fact]
    public void Exhaustion_respects_stall_ratio_threshold()
    {
        var config = new EventGrammarConfig { ExhaustionWindow = 6, ExhaustionStallRatio = 0.05 }; // very strict
        var detector = new ExhaustionDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42100, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42200, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42300, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(400, "BTCUSDT", 42310, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(500, "BTCUSDT", 42315, 1, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(600, "BTCUSDT", 42305, 1, 0, 0, 0));

        // Range ratio = (42315-42305)/(42300-42000) = 10/300 ≈ 0.033 < 0.05
        // Should fire because it's very stalled
        Assert.Single(results);
    }
}
