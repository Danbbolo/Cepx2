using CEPx.Core;
using CEPx.Pipeline;

Console.WriteLine("Fetching 1000 BTCUSDT 1m candles...");
MarketEvent[] ticks;
try { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000); }
catch (Exception ex) { Console.WriteLine($"API failed: {ex.Message}"); return; }

double THR = 0.4;

foreach (bool useTP in new[] { false, true })
{
    var window = new List<MarketEvent>();
    int trades = 0, wins = 0, stops = 0, timeouts = 0, tpExits = 0;
    double totalPnl = 0;
    bool ip = false; double entry = 0, rawEntry = 0; int et = 0;
    string mode = useTP ? "WITH TP" : "NO TP";

    for (int i = 0; i < ticks.Length; i++)
    {
        var tick = ticks[i];
        window.Add(tick);
        if (window.Count < 5) continue;
        var fw = window.TakeLast(5).ToArray();
        var ff = window.ToArray();

        if (ip)
        {
            double unrealPnl = (tick.Price - rawEntry) / rawEntry * 100;
            bool ex = false; string why = "";

            if (useTP && unrealPnl > 0.3) { ex = true; why = "tp"; tpExits++; }
            else if (i - et > 20) { ex = true; why = "timeout"; timeouts++; }
            else if (unrealPnl < -0.5) { ex = true; why = "stoploss"; stops++; }

            if (ex)
            {
                double exitPx = tick.Price * 0.9999;
                double pnl = (exitPx - entry) / entry * 100 - 0.1;
                totalPnl += pnl; trades++; if (pnl > 0) wins++;
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
    double avg = trades > 0 ? totalPnl / trades : 0;
    Console.WriteLine($"\n{mode}: thr={THR:F1}% trades={trades} win={wr:F0}% pnl={avg:F2}% tp={tpExits} timeout={timeouts} stop={stops}");
}

// Detail on WITH TP
Console.WriteLine($"\n=== WITH TP TRADE DETAIL ===");
var dw = new List<MarketEvent>();
int tn = 0; bool ipp = false; double ee = 0, re = 0; int eet = 0;
for (int i = 0; i < ticks.Length && tn < 8; i++)
{
    var tick = ticks[i];
    dw.Add(tick);
    if (dw.Count < 5) continue;
    var fw = dw.TakeLast(5).ToArray();
    var ff = dw.ToArray();

    if (ipp)
    {
        double u = (tick.Price - re) / re * 100;
        bool ex = false; string why = "";
        if (u > 0.3) { ex = true; why = "tp"; }
        else if (i - eet > 20) { ex = true; why = "timeout"; }
        else if (u < -0.5) { ex = true; why = "stoploss"; }
        if (ex)
        {
            tn++; double ep = tick.Price * 0.9999;
            double pp = (ep - ee) / ee * 100 - 0.1;
            Console.WriteLine($"Trade {tn}: entry={re:F2} exit={tick.Price:F2} pnl={pp:F2}% {why} hold={i-eet}t");
            ipp = false; continue;
        }
    }

    var sw = PipelineFunctions.DetectSweepStart(fw);
    if (!sw.HasValue || ipp) continue;
    var sc = PipelineFunctions.ScoreEvent(sw.Value, ff);
    if (sc.PatternSimilarity < THR) continue;
    re = tick.Price; ee = tick.Price * 1.0001; ipp = true; eet = i;
}