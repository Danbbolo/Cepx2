using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;

var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");

var window = new List<MarketEvent>();
foreach (var tick in ticks)
{
    window.Add(tick);
    if (window.Count < 5) continue;

    var w = window.TakeLast(5).ToArray();
    var sweep = PipelineFunctions.DetectSweepStart(w);
    if (!sweep.HasValue) continue;

    var full = window.ToArray();
    var score = PipelineFunctions.ScoreEvent(sweep.Value, full);
    var state = PipelineFunctions.WriteState(score, full);
    var decision = PolicyEngine.Decide(state);

    PolicyEngine.PaperExecute(decision, tick.Price);
}

if (PolicyEngine.InPosition)
{
    PolicyEngine.PaperExecute(
        new PolicyDecision(0, "BTCUSDT", "exit", "", "forced", 0),
        ticks.Last().Price);
}

PolicyEngine.PrintPaperSummary();
