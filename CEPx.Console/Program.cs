using CEPx.Core;
using CEPx.Pipeline;

var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var ticks = new MarketEvent[]
{
    new MarketEvent(now,     "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
    new MarketEvent(now+100, "BTCUSDT", 42030.0, 1.2, 0, 0, 0),
    new MarketEvent(now+200, "BTCUSDT", 42080.0, 1.5, 0, 0, 0),
    new MarketEvent(now+300, "BTCUSDT", 42150.0, 2.1, 0, 0, 0),
    new MarketEvent(now+400, "BTCUSDT", 42300.0, 5.0, 0, 0, 0),
};

var events = PipelineFunctions.RunPipeline(ticks);

if (events.Length == 0)
    Console.WriteLine("CEPx: no SweepStart detected");
else
    foreach (var e in events)
        Console.WriteLine($"CEPx: {e.Type} on {e.Symbol} @ {e.Price}");
