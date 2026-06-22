using CEPx.Core;
using CEPx.Pipeline;

var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var ticks = new MarketEvent[]
{
    new(now,     "BTCUSDT", 42000.0, 1.0, 0, 0, 0),
    new(now+100, "BTCUSDT", 42030.0, 1.2, 0, 0, 0),
    new(now+200, "BTCUSDT", 42080.0, 1.5, 0, 0, 0),
    new(now+300, "BTCUSDT", 42150.0, 2.1, 0, 0, 0),
    new(now+400, "BTCUSDT", 42300.0, 5.0, 0, 0, 0),
    new(now+500, "BTCUSDT", 42350.0, 3.0, 0, 0, 0),
    new(now+600, "BTCUSDT", 42400.0, 2.5, 0, 0, 0),
    new(now+700, "BTCUSDT", 42450.0, 2.0, 0, 0, 0),
    new(now+800, "BTCUSDT", 42500.0, 1.8, 0, 0, 0),
    new(now+900, "BTCUSDT", 42550.0, 1.5, 0, 0, 0),
};
var sweep = PipelineFunctions.DetectSweepStart(ticks);
if (sweep.HasValue)
{
    var score = PipelineFunctions.ScoreEvent(sweep.Value, ticks);
    Console.WriteLine($"Kalman: mean={score.StateMean:F2} vel={score.StateVelocity:F2} upper={score.UncertaintyUpper:F2} lower={score.UncertaintyLower:F2}");
}
