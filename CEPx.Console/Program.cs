using CEPx.Core;
using CEPx.Pipeline;

// April 12-13 2024 — Iran-Israel liquidation cascade
long start = 1712880000000;
long end   = 1713052740000;

Console.WriteLine("Fetching BTCUSDT 1m — April 12-13 2024...");
MarketEvent[] ticks;
try { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, start, end); }
catch (Exception ex) { Console.WriteLine($"API: {ex.Message}"); return; }

var high = ticks.Max(t => t.Price); var low = ticks.Min(t => t.Price);
Console.WriteLine($"Period: Apr 12-13 2024 | Range: {low:F0}-{high:F0} | Ticks: {ticks.Length}");

double THR = 0.4; double SL = 0.5;
double[] tpLevels = { 0.5, 1.0, 1.5 };

// Count sweeps
int sweeps = 0;
var sw = new List<MarketEvent>();
for (int i = 0; i < ticks.Length; i++) { sw.Add(ticks[i]); if (sw.Count < 5) continue; if (PipelineFunctions.DetectSweepStart(sw.TakeLast(5).ToArray()) != null) sweeps++; }
Console.WriteLine($"Sweeps detected: {sweeps}");

Console.WriteLine($"\n=== TP COMPARISON (thr={THR:F1}% sl={SL:F1}%) ===\n");

foreach (double tp in tpLevels)
{
    var window = new List<MarketEvent>();
    int trades = 0, wins = 0, stops = 0, timeouts = 0, tpExits = 0;
    double totalPnl = 0;
    bool ip = false; double entry = 0, rawEntry = 0; int et = 0;

    for (int i = 0; i < ticks.Length; i++)
    {
        var tick = ticks[i]; window.Add(tick);
        if (window.Count < 5) continue;
        var fw = window.TakeLast(5).ToArray();
        var ff = window.ToArray();

        if (ip)
        {
            double u = (tick.Price - rawEntry) / rawEntry * 100;
            bool ex = false;
            if (u > tp) { ex = true; tpExits++; }
            else if (i - et > 20) { ex = true; timeouts++; }
            else if (u < -SL) { ex = true; stops++; }
            if (ex) { double ep = tick.Price * 0.9999; double pp = (ep - entry) / entry * 100 - 0.1; totalPnl += pp; trades++; if (pp > 0) wins++; ip = false; continue; }
        }

        var sweep = PipelineFunctions.DetectSweepStart(fw);
        if (!sweep.HasValue || ip) continue;
        var sc = PipelineFunctions.ScoreEvent(sweep.Value, ff);
        if (sc.PatternSimilarity < THR) continue;
        rawEntry = tick.Price; entry = tick.Price * 1.0001; ip = true; et = i;
    }

    double wr = trades > 0 ? (double)wins / trades * 100 : 0;
    double avgPnl = trades > 0 ? totalPnl / trades : 0;
    Console.WriteLine($"TP={tp:F1}% | {trades,2} trades, {wr,3:F0}% win, {avgPnl,6:F2}% avg PnL, TP={tpExits} SL={stops} TO={timeouts}");
}