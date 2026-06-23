using CEPx.Core;
using CEPx.Pipeline;
using CEPx.Policy;
using CEPx.Scoring;
using CEPx.EventGrammar;

// ── Multi-day test dates (add/remove as needed) ──
var testDates = new[] {
    (2026, 6, 16), (2026, 6, 17), (2026, 6, 18),
    (2026, 6, 19), (2026, 6, 20), (2026, 6, 21), (2026, 6, 22)
};

var allResults = new List<DayResult>();

foreach (var (y, m, d) in testDates)
{
    var result = RunDay(y, m, d);
    allResults.Add(result);
}

// ── Aggregated Summary ──
Console.WriteLine("\n========================================");
Console.WriteLine("=== MULTI-DAY AGGREGATED SUMMARY ===");
Console.WriteLine("========================================");
Console.WriteLine($"{"Date",-12} {"Trades",7} {"Win%",6} {"PnL%",8} {"ModeA",6} {"ModeB",6} {"mom",4} {"vel",4} {"rev",4}");
Console.WriteLine(new string('-', 70));

int totalTrades = 0, totalWins = 0, totalModeA = 0, totalModeB = 0;
double totalPnl = 0;
int totalMom = 0, totalVel = 0, totalRev = 0, totalOther = 0;

foreach (var r in allResults)
{
    Console.WriteLine($"{r.Date,-12} {r.Trades,7} {r.WinRate,5:F0}% {r.PnL,7:F2}% {r.ModeA,6} {r.ModeB,6} {r.MomDecay,4} {r.VelFlip,4} {r.RevSig,4}");
    totalTrades += r.Trades;
    totalWins += r.Wins;
    totalPnl += r.PnL;
    totalModeA += r.ModeA;
    totalModeB += r.ModeB;
    totalMom += r.MomDecay;
    totalVel += r.VelFlip;
    totalRev += r.RevSig;
    totalOther += r.OtherExits;
}

Console.WriteLine(new string('-', 70));
double avgWin = totalTrades > 0 ? (double)totalWins / totalTrades * 100 : 0;
double avgPnlPerDay = allResults.Count > 0 ? totalPnl / allResults.Count : 0;
Console.WriteLine($"{"TOTAL",-12} {totalTrades,7} {avgWin,5:F0}% {totalPnl,7:F2}% {totalModeA,6} {totalModeB,6} {totalMom,4} {totalVel,4} {totalRev,4}");
Console.WriteLine($"\nAvg PnL/day: {avgPnlPerDay:F2}% | Win rate: {avgWin:F0}%");
Console.WriteLine($"Modes: mode_a={totalModeA} | mode_b={totalModeB}");
Console.WriteLine($"Mode A with cont signal: {PolicyEngine.ModeAWithContSignal}/{totalModeA}");
Console.WriteLine($"Mode B with rev signal: {PolicyEngine.ModeBWithRevSignal}/{totalModeB}");
Console.WriteLine($"Exits: momentum_decay={totalMom} velocity_flip={totalVel} reversal_signal={totalRev} other={totalOther}");

// ── Signal activity summary ──
Console.WriteLine($"\nSignal activity:");
Console.WriteLine($"  Reversal: Absorption={PolicyEngine.AbsorptionCount} Exhaustion={PolicyEngine.ExhaustionCount} Reclaim={PolicyEngine.ReclaimCount} LiqCluster={PolicyEngine.LiquidationClusterCount}");
Console.WriteLine($"  Continuation: MomentumPersistence={PolicyEngine.MomentumPersistenceCount} NoAbsorption={PolicyEngine.NoAbsorptionCount}");

// ── Day runner ──
static DayResult RunDay(int year, int month, int day)
{
    string dateLabel = $"{year}-{month:D2}-{day:D2}";
    long startUTC = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    long endUTC = new DateTimeOffset(year, month, day, 23, 59, 59, TimeSpan.Zero).ToUnixTimeMilliseconds();
    var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, startMs: startUTC, endMs: endUTC);
    if (ticks.Length < 100) ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000);

    // CHD_SPIKE: use CHD CSV if path provided via env var (remove after full integration)
    var chdCsvDir = Environment.GetEnvironmentVariable("CHD_CSV_DIR");
    if (!string.IsNullOrEmpty(chdCsvDir))
    {
        var chdPath = Path.Combine(chdCsvDir, $"{dateLabel}.csv");
        if (File.Exists(chdPath))
        {
            ticks = PipelineFunctions.FetchChdCsv(chdPath);
        }
        else
            Console.WriteLine($"[CHD_SPIKE] Missing: {chdPath} — falling back to Binance");
    }
    // END CHD_SPIKE

    // Fetch liquidations for the same time window
    // DEBUG_LIQ: Temporary logging to assess liquidation data quality. Remove after analysis.
    LiquidationEvent[] liquidations;
    try { liquidations = PipelineFunctions.FetchLiquidations("BTCUSDT", 100, startMs: startUTC, endMs: endUTC); }
    catch (Exception ex) { Console.WriteLine($"[DEBUG_LIQ] Fetch failed: {ex.Message}"); liquidations = Array.Empty<LiquidationEvent>(); }

    // DEBUG_LIQ: Per-day summary
    int liqLongCount = liquidations.Count(l => l.IsLongLiquidation);
    int liqShortCount = liquidations.Count(l => l.IsShortLiquidation);
    double liqAvgQty = liquidations.Length > 0 ? liquidations.Average(l => l.Quantity) : 0;
    double liqMaxQty = liquidations.Length > 0 ? liquidations.Max(l => l.Quantity) : 0;
    Console.WriteLine($"[DEBUG_LIQ] Day summary: {liquidations.Length} total | long={liqLongCount} short={liqShortCount} | avgQty={liqAvgQty:F1} maxQty={liqMaxQty:F1}");
    // END DEBUG_LIQ

    Console.WriteLine($"\n=== {dateLabel} — {ticks.Length} candles ===");

    PolicyEngine.Reset();
    // DIAG: set data source after Reset (which clears it)
    PolicyEngine.DiagDataSource = string.IsNullOrEmpty(chdCsvDir)
        ? $"Binance klines ({dateLabel})"
        : $"CHD ({dateLabel})";
    PolicyEngine.DiagCandleCount = ticks.Length;
    // END DIAG

    const long POST_SWEEP_WINDOW_MS = 600_000; // 10 minutes — monitor for events after sweep
    var buf = new MarketEvent[10];
    double pendingSweepOrigin = 0;
    bool pendingSweepIsBullish = false;
    long postSweepEndMs = 0; // absolute timestamp — monitor until this time
    long postSweepEndTick = 0; // tick index when window closes

    for (int i = 0; i < ticks.Length; i++)
    {
        buf[i % 10] = ticks[i];
        long nowMs = ticks[i].Timestamp;

        // ── Post-sweep detectors: time-based window after sweep ──
        if (nowMs <= postSweepEndMs && i >= 9)
        {
            var w10 = new MarketEvent[10];
            for (int j = 0; j < 10; j++) w10[j] = buf[(i - 9 + j) % 10];

            var reclaim = PipelineFunctions.DetectReclaim(w10, pendingSweepOrigin, pendingSweepIsBullish);
            if (reclaim != null) PolicyEngine.RecordEvent(reclaim.Value);
            var absorption = PipelineFunctions.DetectAbsorption(w10);
            if (absorption != null) PolicyEngine.RecordEvent(absorption.Value);
            var exhaustion = PipelineFunctions.DetectExhaustionPulse(w10);
            if (exhaustion != null) PolicyEngine.RecordEvent(exhaustion.Value);

            // Liquidation cluster check
            var liqCluster = LiquidationClusterDetector.Detect(
                w10, liquidations, pendingSweepOrigin, pendingSweepIsBullish,
                ticks[i].Timestamp - 600_000, ticks[i].Timestamp);
            if (liqCluster != null) PolicyEngine.RecordEvent(liqCluster.Value);

            // Continuation detectors: record BEFORE re-evaluation so they're active for Decide
            var momPer = MomentumPersistenceDetector.Detect(w10, 0.0);
            if (momPer != null) PolicyEngine.RecordEvent(momPer.Value);
            var noAbs = NoMeaningfulAbsorptionDetector.Detect(w10);
            if (noAbs != null) PolicyEngine.RecordEvent(noAbs.Value);

            // ── Strong reversal signal → re-evaluate with current window ──
            if (exhaustion != null || liqCluster != null || absorption != null || reclaim != null)
            {
                PolicyEngine.DiagReEvalAttempts++;
                var freshState = ScoringEngine.RefreshState(w10, pendingSweepIsBullish);
                // Update candidate with fresh state + signal flags
                PolicyEngine.UpdateCandidate(freshState,
                    exhaustion != null, absorption != null, reclaim != null,
                    momPer != null, noAbs != null);
                var reDecision = PolicyEngine.Decide(freshState, i, ticks[i].Price,
                    pendingSweepOrigin, pendingSweepIsBullish);
                if (reDecision.Action == "enter")
                {
                    PolicyEngine.DiagReEvalEntries++;
                    PolicyEngine.PaperExecute(reDecision, ticks[i].Price, "sweep",
                        freshState.PatternSimilarity, freshState.KalmanVelocity, i,
                        pendingSweepOrigin, pendingSweepIsBullish);
                }
            }
        }
        else
        {
            // Post-sweep window closed — finalize pending candidate
            if (PolicyEngine.DiagReEvalAttempts > 0 || i > postSweepEndTick + 1)
            {
                var finalDecision = PolicyEngine.FinalizeCandidate(i, ticks[i].Price);
                if (finalDecision.Action == "enter")
                {
                    PolicyEngine.PaperExecute(finalDecision, ticks[i].Price, "sweep",
                        0, 0, i, pendingSweepOrigin, pendingSweepIsBullish);
                }
            }
        }

        // ── Exit check when in position ──
        if (PolicyEngine.InPosition && i >= 9)
        {
            var w10 = new MarketEvent[10];
            for (int j = 0; j < 10; j++) w10[j] = buf[(i - 9 + j) % 10];
            var exitSweep = new CepEvent(ticks[i].Timestamp, "BTCUSDT", "SweepStart", ticks[i].Price, "");
            var exitScore = ScoringEngine.ScoreEvent(exitSweep, w10);
            var exitState = ScoringEngine.WriteState(exitScore, w10);
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

        // Track sweep for post-sweep detectors — time-based window
        pendingSweepOrigin = w5[0].Price;
        pendingSweepIsBullish = sweep.Value.Context == "bullish";
        postSweepEndMs = sweep.Value.Timestamp + POST_SWEEP_WINDOW_MS;
        postSweepEndTick = i + 10; // ~10 minutes on 1m candles

        if (i < 9) continue;
        var scoreW10 = new MarketEvent[10];
        for (int j = 0; j < 10; j++) scoreW10[j] = buf[(i - 9 + j) % 10];
        var score = ScoringEngine.ScoreEvent(sweep.Value, scoreW10);
        var state = ScoringEngine.WriteState(score, scoreW10);

        double sweepOrigin = w5[0].Price;
        bool isBullish = sweep.Value.Context == "bullish";

        // Create candidate — defer entry decision to post-sweep window close
        if (!PolicyEngine.InPosition)
            PolicyEngine.CreateCandidate(i, postSweepEndTick, sweepOrigin, isBullish, state);
    }

    PolicyEngine.PrintPaperSummary();
    // DIAG: print structural diagnostics if enabled
    if (Environment.GetEnvironmentVariable("DIAG_MODE") == "1")
        PolicyEngine.PrintDiagnostics();
    // END DIAG
    return new DayResult(dateLabel, PolicyEngine.TotalTrades, PolicyEngine.WinningTrades,
        PolicyEngine.TotalPnL, PolicyEngine.ModeACount, PolicyEngine.ModeBCount,
        PolicyEngine.MomDecayExits, PolicyEngine.VelFlipExits, PolicyEngine.RevSigExits, PolicyEngine.OtherExits);
}

record DayResult(string Date, int Trades, int Wins, double PnL,
    int ModeA, int ModeB, int MomDecay, int VelFlip, int RevSig, int OtherExits)
{
    public double WinRate => Trades > 0 ? (double)Wins / Trades * 100 : 0;
}
