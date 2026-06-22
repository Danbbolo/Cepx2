using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;

// ── June 22 2026 BTC/USDT 1m — two-mode BT ──
long startUTC = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
long endUTC = new DateTimeOffset(2026, 6, 22, 23, 59, 59, TimeSpan.Zero).ToUnixTimeMilliseconds();
var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, startMs: startUTC, endMs: endUTC);
if (ticks.Length < 100) ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000);

Console.WriteLine($"=== TWO-MODE BT — June 22, {ticks.Length} candles ===\n");

// Reset all state
PolicyEngine.InPosition = false;
PolicyEngine.PositionSide = "";
PolicyEngine.EntryPrice = 0;
PolicyEngine.EntryTick = 0;

var detectorHits = new List<(CepEvent sweep, int tickIdx)>();
var btEntries = new List<(CepEvent sweep, int tickIdx)>();
var btRejects = new List<(CepEvent sweep, string reason, int tickIdx)>();
var buf = new MarketEvent[10];

for (int i = 0; i < ticks.Length; i++)
{
    buf[i % 10] = ticks[i];

    if (PolicyEngine.InPosition && i >= 9)
    {
        var w10 = new MarketEvent[10];
        for (int j = 0; j < 10; j++) w10[j] = buf[(i - 9 + j) % 10];
        var exitSweep = new CepEvent(ticks[i].Timestamp, "BTCUSDT", "SweepStart", ticks[i].Price, "");
        var exitScore = PipelineFunctions.ScoreEvent(exitSweep, w10);
        var exitState = PipelineFunctions.WriteState(exitScore, w10);
        PolicyEngine.RecordPatternSimilarity(exitState.PatternSimilarity);
        var exitDecision = PolicyEngine.Decide(exitState, i, ticks[i].Price);
        if (exitDecision.Action == "exit")
            PolicyEngine.PaperExecute(exitDecision, ticks[i].Price, "exit", exitScore.PatternSimilarity, exitScore.StateVelocity, i);
    }

    if (i < 5) continue;
    var w5 = new MarketEvent[5];
    for (int j = 0; j < 5; j++) w5[j] = buf[(i - 4 + j) % 10];
    var sweep = PipelineFunctions.DetectSweepStart(w5);
    if (sweep == null) continue;
    detectorHits.Add((sweep.Value, i));

    if (i < 9) continue;
    var scoreW10 = new MarketEvent[10];
    for (int j = 0; j < 10; j++) scoreW10[j] = buf[(i - 9 + j) % 10];
    var score = PipelineFunctions.ScoreEvent(sweep.Value, scoreW10);
    var state = PipelineFunctions.WriteState(score, scoreW10);

    double sweepOrigin = w5[0].Price;
    bool isBullish = sweep.Value.Context == "bullish";
    var decision = PolicyEngine.Decide(state, i, ticks[i].Price, sweepOrigin, isBullish);

    if (decision.Action == "enter")
    {
        PolicyEngine.PaperExecute(decision, ticks[i].Price, "sweep", score.PatternSimilarity, score.StateVelocity, i, sweepOrigin, isBullish);
        btEntries.Add((sweep.Value, i));
    }
    else if (PolicyEngine.InPosition)
    {
        continue;
    }
    else
    {
        string reason;
        if (state.ReversalScore >= 0.5) reason = "reversal_score_high";
        else if (state.PatternSimilarity < 0.35) reason = "similarity_too_low";
        else reason = "other";
        btRejects.Add((sweep.Value, reason, i));
    }
}

Console.WriteLine($"Detector hits: {detectorHits.Count}");
Console.WriteLine($"BT entries:    {btEntries.Count}");
Console.WriteLine($"BT rejects:    {btRejects.Count}\n");

Console.WriteLine("=== BT ENTRIES ===");
foreach (var (sweep, idx) in btEntries)
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sweep.Timestamp).ToOffset(TimeSpan.FromHours(2));
    Console.WriteLine($"  {dt:HH:mm:ss} CEST | dir={sweep.Context,-8} | px={sweep.Price:F0}");
}

Console.WriteLine($"\n=== PAPER TRADING SUMMARY ===");
PolicyEngine.PrintPaperSummary();
PolicyEngine.PrintEvidenceReport();