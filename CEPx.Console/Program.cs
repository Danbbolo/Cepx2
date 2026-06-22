using CEPx.Core;
using CEPx.Pipeline;

Console.WriteLine("Fetching 1000 BTCUSDT 1m candles...");
MarketEvent[] ticks;
try { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000); }
catch (Exception ex) { Console.WriteLine($"API failed: {ex.Message}"); return; }

double THR = 0.4;
double SL = 0.5;
double[] tpLevels = { 0.5, 1.0, 1.5 };

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

            if (ex)
            {
                double ep = tick.Price * 0.9999;
                double pp = (ep - entry) / entry * 100 - 0.1;
                totalPnl += pp; trades++; if (pp > 0) wins++;
                ip = false; continue;
            }
        }

        var sw = PipelineFunctions.DetectSweepStart(fw);
        if (!sw.HasValue || ip) continue;
        var sc = PipelineFunctions.ScoreEvent(sw.Value, ff);
        if (sc.PatternSimilarity < THR) continue;
        rawEntry = tick.Price; entry = tick.Price * 1.0001; ip = true; et = i;
    }

    double wr = trades > 0 ? (double)wins / trades * 100 : 0;
    double avgPnl = trades > 0 ? totalPnl / trades : 0;
    double tpRate = trades > 0 ? (double)tpExits / trades * 100 : 0;
    double slRate = trades > 0 ? (double)stops / trades * 100 : 0;
    double toRate = trades > 0 ? (double)timeouts / trades * 100 : 0;
    Console.WriteLine($"TP={tp:F1}% | trades={trades,2} win={wr,3:F0}% pnl={avgPnl,6:F2}% tp={tpRate,3:F0}% sl={slRate,3:F0}% to={toRate,3:F0}%");
}