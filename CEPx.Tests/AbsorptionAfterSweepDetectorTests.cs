using System.Reactive.Subjects;
using CEPx.Core;
using CEPx.EventGrammar;
using Xunit;

namespace CEPx.Tests;

public class AbsorptionAfterSweepDetectorTests
{
    [Fact]
    public void Absorption_fires_on_volume_spike_with_minimal_price_move()
    {
        var config = new EventGrammarConfig
        {
            AbsorptionWindow = 5,
            AbsorptionVolumeMultiplier = 3.0,
            SweepPctThreshold = 0.1
        };
        var detector = new AbsorptionAfterSweepDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        // Bullish sweep at 42000
        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        // 4 normal-volume ticks, then 1 high-volume tick with minimal price change
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42010, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42005, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42015, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(400, "BTCUSDT", 42010, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(500, "BTCUSDT", 42020, 4.0, 0, 0, 0)); // high vol, tiny move

        Assert.Single(results);
        Assert.Equal("AbsorptionAfterSweep", results[0].Type);
    }

    [Fact]
    public void Absorption_does_not_fire_if_price_continues_strongly()
    {
        var config = new EventGrammarConfig
        {
            AbsorptionWindow = 5,
            AbsorptionVolumeMultiplier = 3.0,
            SweepPctThreshold = 0.1
        };
        var detector = new AbsorptionAfterSweepDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        // Price moves > 0.1% — too much for absorption
        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42100, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42150, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42200, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(400, "BTCUSDT", 42250, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(500, "BTCUSDT", 42300, 5.0, 0, 0, 0)); // high vol but big move

        Assert.Empty(results);
    }

    [Fact]
    public void Absorption_does_not_fire_on_low_volume()
    {
        var config = new EventGrammarConfig
        {
            AbsorptionWindow = 3,
            AbsorptionVolumeMultiplier = 3.0,
            SweepPctThreshold = 0.1
        };
        var detector = new AbsorptionAfterSweepDetector(config);

        var sweepStream = new Subject<CepEvent>();
        var tickStream = new Subject<MarketEvent>();
        var results = new List<CepEvent>();
        detector.Detect(sweepStream, tickStream).Subscribe(e => results.Add(e));

        sweepStream.OnNext(new CepEvent(0, "BTCUSDT", "SweepStart", 42000, "bullish"));

        tickStream.OnNext(new MarketEvent(100, "BTCUSDT", 42005, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(200, "BTCUSDT", 42010, 1.0, 0, 0, 0));
        tickStream.OnNext(new MarketEvent(300, "BTCUSDT", 42015, 1.5, 0, 0, 0)); // normal vol

        Assert.Empty(results);
    }
}
