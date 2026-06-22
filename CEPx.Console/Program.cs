using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;

// ── Fetch June 18 2026 data (CEST = UTC+2, so June 18 00:00 CEST = June 17 22:00 UTC) ──
long tStart = 1782422400000; // June 18 00:00 UTC
long tEnd   = 1782508800000; // June 19 00:00 UTC
var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, tStart, tEnd);
if (ticks.Length < 100) ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000);

Console.WriteLine($"=== DETECTOR + BT VERIFICATION (June 18, {ticks.Length} candles) ===\n");

// ── Phase 1: Pure detector (L2) — catch ALL sweeps ──
var detectorHits = new List<(CepEvent sweep, int tickIdx)>();
var buf = new MarketEvent[10]; // 10-candle window for scoring
for (int i = 0; i < ticks.Length; i++)
{
    buf[i % 10] = ticks[i];
    if (i < 5) continue; // need 5 for sweep detection
    // Last 5 for sweep detection
    var w5 = new MarketEvent[5];
    for (int j = 0; j < 5; j++) w5[j] = buf[(i - 4 + j) % 10];
    var sweep = PipelineFunctions.DetectSweepStart(w5);
    if (sweep != null)
        detectorHits.Add((sweep.Value, i));
}

// ── Phase 2: Score + BT filter (L2.5 + L4) ──
var btEntries = new List<(CepEvent sweep, StructuralScore score, BlackboardState state, PolicyDecision decision, int tickIdx)>();
var btRejects = new List<(CepEvent sweep, StructuralScore score, string reason, int tickIdx)>();

foreach (var (sweep, idx) in detectorHits)
{
    // Build 10-candle window ending at sweep
    if (idx < 9) continue; // need 10 candles
    var w10 = new MarketEvent[10];
    for (int j = 0; j < 10; j++) w10[j] = ticks[idx - 9 + j];
    
    var score = PipelineFunctions.ScoreEvent(sweep, w10);
    var state = PipelineFunctions.WriteState(score, w10);
    var decision = PolicyEngine.Decide(state);
    
    if (decision.Action == "enter")
        btEntries.Add((sweep, score, state, decision, idx));
    else
    {
        string reason = "similarity_too_low";
        if (state.ReversalScore >= 0.5) reason = "reversal_score_high";
        else if (state.PatternSimilarity < 0.35) reason = "similarity_too_low";
        btRejects.Add((sweep, score, reason, idx));
    }
}

// ── Report ──
Console.WriteLine($"Detector hits (all sweeps): {detectorHits.Count}");
Console.WriteLine($"BT entries (filtered):    {btEntries.Count}");
Console.WriteLine($"BT rejects:               {btRejects.Count}");
double precision = detectorHits.Count > 0 ? (double)btEntries.Count / detectorHits.Count * 100 : 0;
Console.WriteLine($"BT precision:             {precision:F1}% (entries / detector hits)");
Console.WriteLine();

// Show BT entries
Console.WriteLine("=== BT ENTRIES (continuation sweeps) ===\n");
foreach (var (sweep, score, state, _, idx) in btEntries)
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sweep.Timestamp).ToOffset(TimeSpan.FromHours(2));
    Console.WriteLine($"  {dt:HH:mm:ss} CEST | dir={sweep.Context,-8} | contSim={score.PatternSimilarity:F3} | revSim={score.ReversalSimilarity:F3} | px={sweep.Price:F0}");
}
if (btEntries.Count == 0) Console.WriteLine("  (none)");

// Show BT rejects
Console.WriteLine($"\n=== BT REJECTS ({btRejects.Count}) ===\n");
foreach (var (sweep, score, reason, idx) in btRejects)
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sweep.Timestamp).ToOffset(TimeSpan.FromHours(2));
    Console.WriteLine($"  {dt:HH:mm:ss} CEST | dir={sweep.Context,-8} | contSim={score.PatternSimilarity:F3} | revSim={score.ReversalSimilarity:F3} | reason={reason} | px={sweep.Price:F0}");
}

Console.WriteLine($"\n=== SUMMARY ===");
Console.WriteLine($"Detector recall:  {detectorHits.Count} sweeps caught (pure detector, no filtering)");
Console.WriteLine($"BT precision:     {btEntries.Count}/{detectorHits.Count} entries ({precision:F1}%)");
Console.WriteLine($"Rejects by reversal: {btRejects.Count(r => r.reason == "reversal_score_high")}");
Console.WriteLine($"Rejects by low sim:  {btRejects.Count(r => r.reason == "similarity_too_low")}");