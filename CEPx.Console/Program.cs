using CEPx.Core;
using CEPx.Pipeline;

Console.WriteLine("Fetching 1000 BTCUSDT 1m candles...");
MarketEvent[] ticks;
try { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000); }
catch (Exception ex) { Console.WriteLine($"API failed: {ex.Message}"); return; }
Console.WriteLine($"Loaded {ticks.Length} ticks.");

double[] thresholds = { 0.2, 0.3, 0.4, 0.5 };
var results = new (double thr, int trades, int wins, double pnl, int stops, int timeouts, int sweeps)[thresholds.Length];

for (int ti = 0; ti < thresholds.Length; ti++)
{
    double thr = thresholds[ti];
    var window = new List<MarketEvent>();
    int trades = 0, wins = 0, stops = 0, timeouts = 0, sweeps = 0;
    double totalPnl = 0;
    bool inPos = false; double entry = 0, rawEntry = 0; int entryTick = 0;

    for (int tickIdx = 0; tickIdx < ticks.Length; tickIdx++)
    {
        var tick = ticks[tickIdx];
        window.Add(tick);
        if (window.Count < 5) continue;

        var w = window.TakeLast(5).ToArray();
        var full = window.ToArray();

        if (inPos)
        {
            bool exit = false;
            if (tickIdx - entryTick > 20) { exit = true; timeouts++; }
            else
            {
                double pnl = (tick.Price - rawEntry) / rawEntry * 100;
                if (pnl < -0.5) { exit = true; stops++; }
            }
            if (exit)
            {
                double exitPx = tick.Price * 0.9999;
                double pnl = (exitPx - entry) / entry * 100 - 0.1;
                totalPnl += pnl;
                trades++;
                if (pnl > 0) wins++;
                inPos = false;
                continue;
            }
        }

        var sweep = PipelineFunctions.DetectSweepStart(w);
        if (!sweep.HasValue) continue;
        sweeps++;

        if (inPos) continue;

        var score = PipelineFunctions.ScoreEvent(sweep.Value, full);
        if (score.PatternSimilarity < thr) continue;

        rawEntry = tick.Price;
        entry = tick.Price * 1.0001;
        inPos = true;
        entryTick = tickIdx;
    }
    results[ti] = (thr, trades, wins, totalPnl, stops, timeouts, sweeps);
}

Console.WriteLine();
Console.WriteLine("=== THRESHOLD COMPARISON ===");
Console.WriteLine($"{"Thr",6} {"Trades",7} {"Win%",7} {"PnL%",7} {"Stops",7} {"TimeOuts",9} {"Sweeps",7}");
foreach (var r in results)
{
    double wr = r.trades > 0 ? (double)r.wins / r.trades * 100 : 0;
    Console.WriteLine($"{r.thr,6:F1}% {r.trades,7} {wr,6:F0}% {r.pnl,6:F2}% {r.stops,7} {r.timeouts,9} {r.sweeps,7}");
}

// Per-trade detail for first 3 trades at 0.2%
Console.WriteLine();
Console.WriteLine("=== TRADE DETAIL (threshold 0.2%) ===");
var dw = new List<MarketEvent>();
int tradeNum = 0; bool ip = false; double e = 0, re = 0; int et = 0;
for (int i = 0; i < ticks.Length && tradeNum < 5; i++)
{
    var tick = ticks[i];
    dw.Add(tick);
    if (dw.Count < 5) continue;
    var fw = dw.TakeLast(5).ToArray();
    var ff = dw.ToArray();

    if (ip)
    {
        bool ex = false; string why = "";
        if (i - et > 20) { ex = true; why = "timeout"; }
        else { double p = (tick.Price - re) / re * 100; if (p < -0.5) { ex = true; why = "stoploss"; } }
        if (ex)
        {
            tradeNum++;
            double ep = tick.Price * 0.9999;
            double pp = (ep - e) / e * 100 - 0.1;
            Console.WriteLine($"Trade {tradeNum}: enter={re:F2} exit={tick.Price:F2} pnl={pp:F2}% reason={why} hold={i-et}ticks");
            Console.WriteLine($"  Entry candle: {ticks[i-(i-et)].Price:F2} vol={ticks[i-(i-et)].Volume:F3}");
            if (i+1 < ticks.Length) Console.WriteLine($"  +1 candle: {ticks[i+1].Price:F2}");
            ip = false; continue;
        }
    }

    var sw = PipelineFunctions.DetectSweepStart(fw);
    if (!sw.HasValue) continue;
    if (ip) continue;
    var sc = PipelineFunctions.ScoreEvent(sw.Value, ff);
    if (sc.PatternSimilarity < 0.2) continue;
    re = tick.Price; e = tick.Price * 1.0001; ip = true; et = i;
}