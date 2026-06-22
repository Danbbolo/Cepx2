using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;

// ── Fetch June 18 2026 data (CEST = UTC+2, so June 18 00:00 CEST = June 17 22:00 UTC) ──
long tStart = 1782422400000; // June 18 00:00 UTC
long tEnd   = 1782508800000; // June 19 00:00 UTC
var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, tStart, tEnd);
if (ticks.Length < 100) ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000);

Console.WriteLine($"=== DETECTOR + BT VERIFICATION (June 18, {ticks.Length} candles) ===\n");

// Reset paper trading state
PolicyEngine.InPosition = false;
PolicyEngine.PositionSide = "";
PolicyEngine.EntryPrice = 0;
PolicyEngine.EntryTick = 0;

var detectorHits = new List<(CepEvent sweep, int tickIdx)>();
var btEntries = new List<(CepEvent sweep, StructuralScore score, BlackboardState state, PolicyDecision decision, int tickIdx)>();
var btRejects = new List<(CepEvent sweep, StructuralScore score, string reason, int tickIdx)>();

var buf = new MarketEvent[10]; // 10-candle rolling window

for (int i = 0; i < ticks.Length; i++)
{
    buf[i % 10] = ticks[i];

    // ── EXIT CHECK: every tick when in position ──
    if (PolicyEngine.InPosition && i >= 9)
    {
        // Build 10-candle window ending at current tick
        var w10 = new MarketEvent[10];
        for (int j = 0; j < 10; j++) w10[j] = buf[(i - 9 + j) % 10];
        var exitSweep = new CepEvent(ticks[i].Timestamp, "BTCUSDT", "SweepStart", ticks[i].Price, "");
        var exitScore = PipelineFunctions.ScoreEvent(exitSweep, w10);
        var exitState = PipelineFunctions.WriteState(exitScore, w10);
        var exitDecision = PolicyEngine.Decide(exitState, i, ticks[i].Price);
        if (exitDecision.Action == "exit")
        {
            PolicyEngine.PaperExecute(exitDecision, ticks[i].Price, "exit", exitScore.PatternSimilarity, exitScore.StateVelocity, i);
            // After exit, continue to check for re-entry on same tick
        }
    }

    // ── SWEEP DETECTION (L2) ──
    if (i < 5) continue;
    var w5 = new MarketEvent[5];
    for (int j = 0; j < 5; j++) w5[j] = buf[(i - 4 + j) % 10];
    var sweep = PipelineFunctions.DetectSweepStart(w5);
    if (sweep == null) continue;
    detectorHits.Add((sweep.Value, i));

    // ── SCORE + BT FILTER (L2.5 + L4) ──
    if (i < 9) continue; // need 10 candles for scoring
    var scoreW10 = new MarketEvent[10];
    for (int j = 0; j < 10; j++) scoreW10[j] = buf[(i - 9 + j) % 10];
    
    var score = PipelineFunctions.ScoreEvent(sweep.Value, scoreW10);
    var state = PipelineFunctions.WriteState(score, scoreW10);
    var decision = PolicyEngine.Decide(state, i, ticks[i].Price);
    
    if (decision.Action == "enter")
    {
        PolicyEngine.PaperExecute(decision, ticks[i].Price, "sweep", score.PatternSimilarity, score.StateVelocity, i);
        btEntries.Add((sweep.Value, score, state, decision, i));
    }
    else
    {
        string reason;
        if (state.ReversalScore >= 0.5) reason = "reversal_score_high";
        else if (state.PatternSimilarity < 0.35) reason = "similarity_too_low";
        else reason = "other";
        btRejects.Add((sweep.Value, score, reason, i));
    }
}

// ── Report ──
Console.WriteLine($"\nDetector hits (all sweeps): {detectorHits.Count}");
Console.WriteLine($"BT entries (filtered):    {btEntries.Count}");
Console.WriteLine($"BT rejects:               {btRejects.Count}");
double precision = detectorHits.Count > 0 ? (double)btEntries.Count / detectorHits.Count * 100 : 0;
Console.WriteLine($"BT precision:             {precision:F1}% (entries / detector hits)");
Console.WriteLine();

// Show BT entries
Console.WriteLine("=== BT ENTRIES (continuation sweeps) ===\n");
foreach (var (sweep, sc, st, _, idx) in btEntries)
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sweep.Timestamp).ToOffset(TimeSpan.FromHours(2));
    Console.WriteLine($"  {dt:HH:mm:ss} CEST | dir={sweep.Context,-8} | contSim={sc.PatternSimilarity:F3} | revSim={sc.ReversalSimilarity:F3} | px={sweep.Price:F0}");
}
if (btEntries.Count == 0) Console.WriteLine("  (none)");

// Show BT rejects (first 15 only to avoid flooding)
var rejectsToShow = btRejects.Take(15).ToList();
Console.WriteLine($"\n=== BT REJECTS (showing {rejectsToShow.Count} of {btRejects.Count}) ===\n");
foreach (var (sweep, sc, reason, idx) in rejectsToShow)
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sweep.Timestamp).ToOffset(TimeSpan.FromHours(2));
    Console.WriteLine($"  {dt:HH:mm:ss} CEST | dir={sweep.Context,-8} | contSim={sc.PatternSimilarity:F3} | revSim={sc.ReversalSimilarity:F3} | reason={reason} | px={sweep.Price:F0}");
}
if (btRejects.Count > 15) Console.WriteLine($"  ... and {btRejects.Count - 15} more");

Console.WriteLine($"\n=== PAPER TRADING SUMMARY ===");
PolicyEngine.PrintPaperSummary();
Console.WriteLine($"\n=== SUMMARY ===");
Console.WriteLine($"Detector recall:  {detectorHits.Count} sweeps caught (pure detector, no filtering)");
Console.WriteLine($"BT precision:     {btEntries.Count}/{detectorHits.Count} entries ({precision:F1}%)");
Console.WriteLine($"Rejects by reversal: {btRejects.Count(r => r.reason == "reversal_score_high")}");
Console.WriteLine($"Rejects by low sim:  {btRejects.Count(r => r.reason == "similarity_too_low")}");
Console.WriteLine($"Rejects by other:    {btRejects.Count(r => r.reason == "other")}");