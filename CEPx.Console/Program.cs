using CEPx.Core;
using CEPx.Policy;

var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var state = new BlackboardState(
    now, "BTCUSDT", true, "sweep", 0.6981,
    61.57, 42566.12, 42563.64, 0.0,
    "volatile", 0.85, "hold"
);
var decision = PolicyEngine.Decide(state);
Console.WriteLine($"Action: {decision.Action} Side: {decision.Side} Reason: {decision.Reason}");
