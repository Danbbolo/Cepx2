using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;

var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
var window = new List<MarketEvent>();
int tickIdx = 0;

foreach (var tick in ticks)
{
    tickIdx++;
    window.Add(tick);
    if (window.Count < 5) continue;

    var w = window.TakeLast(5).ToArray();
    var sweep = PipelineFunctions.DetectSweepStart(w);
    if (!sweep.HasValue) continue;

    var full = window.ToArray();
    var score = PipelineFunctions.ScoreEvent(sweep.Value, full);
    var state = PipelineFunctions.WriteState(score, full);
    var decision = PolicyEngine.Decide(state);

    PolicyEngine.PaperExecute(decision, tick.Price,
        score.PatternFamily, score.PatternSimilarity, score.StateVelocity, tickIdx);
}

if (PolicyEngine.InPosition)
{
    PolicyEngine.PaperExecute(
        new PolicyDecision(0, "BTCUSDT", "exit", "", "forced", 0),
        ticks.Last().Price, "", 0, 0, tickIdx);
}

PolicyEngine.PrintEvidenceReport();
PolicyEngine.SaveTradeCsv("/tmp/cepx_trades.csv");
