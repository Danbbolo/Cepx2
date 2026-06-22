using CEPx.Core;
using CEPx.Blackboard;

var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var state = new BlackboardState(
    now, "BTCUSDT", true, "sweep", 0.6981,
    61.57, 42566.12, 42563.64, 0.0,
    "volatile", 0.85, "hold"
);
BlackboardWriter.Connect();
BlackboardWriter.Write(state);
var read = BlackboardWriter.Read("BTCUSDT");
Console.WriteLine($"Read back: {read?.PatternFamily} sim={read?.PatternSimilarity:F4}");
