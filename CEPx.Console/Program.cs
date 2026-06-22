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
    var full = window.ToArray();

    if (PolicyEngine.InPosition)
    {
        string? exitReason = null;
        if (PipelineFunctions.DetectReclaim(full, PolicyEngine.RawEntryPrice, PolicyEngine.PositionSide == "long") != null)
            exitReason = "reclaim";
        else if (PipelineFunctions.DetectExhaustionPulse(full) != null)
            exitReason = "exhaustion";
        else if (tickIdx - PolicyEngine.EntryTick > 20)
            exitReason = "timeout";
        else
        {
            double pnl = (tick.Price - PolicyEngine.RawEntryPrice) / PolicyEngine.RawEntryPrice * 100;
            if (PolicyEngine.PositionSide == "short") pnl = -pnl;
            if (pnl < -0.5) exitReason = "stoploss";
        }

        if (exitReason != null)
        {
            PolicyEngine.PaperExecute(
                new PolicyDecision(0, "BTCUSDT", "exit", "", exitReason, 0),
                tick.Price, "", 0, 0, tickIdx);
            continue;
        }
    }

    var sweep = PipelineFunctions.DetectSweepStart(w);
    if (!sweep.HasValue) continue;

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