using CEPx.Core;
using CEPx.Pipeline;
using System.Text;

long tStart = 1712880000000; long tEnd = 1713052740000;
Console.WriteLine("Fetching Apr 12-13 2024...");
var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, tStart, tEnd);
if (ticks.Length < 100) { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000); Console.WriteLine("Used recent data."); }
Console.WriteLine($"{ticks.Length} ticks loaded.");

var sw = new List<MarketEvent>();
var rawWindows = new List<(long ts, double[] prices)>();
for (int i = 0; i < ticks.Length; i++)
{
    sw.Add(ticks[i]); if (sw.Count < 5) continue;
    var sweep = PipelineFunctions.DetectSweepStart(sw.TakeLast(5).ToArray());
    if (sweep == null) continue;
    int w0 = Math.Max(0, i - 5), w1 = Math.Min(ticks.Length - 1, i + 4);
    int cnt = w1 - w0 + 1;
    double[] p = new double[cnt];
    for (int j = 0; j < cnt; j++) p[j] = ticks[w0 + j].Price;
    rawWindows.Add((sweep.Value.Timestamp, p));
}

Console.WriteLine($"Sweeps detected: {rawWindows.Count}");

var allNorm = new List<(long ts, double[] norm, double[] raw)>();
foreach (var (ts, prices) in rawWindows)
{
    double mn = prices.Min(), mx = prices.Max(), r = mx - mn;
    if (r == 0) continue;
    allNorm.Add((ts, prices.Select(v => (v - mn) / r).ToArray(), prices));
}

var clusters = new Dictionary<string, List<(long ts, double[] norm, double[] raw)>>();
foreach (var k in new[] { "smooth_parabolic", "sharp_spike", "v_reversal", "linear_grind" })
    clusters[k] = new();

foreach (var item in allNorm)
{
    var n = item.norm; int L = n.Length; if (L < 5) continue;
    double mid = n[L / 2], s = n[0], e = n[^1];
    int maxIdx = Array.IndexOf(n, n.Max()), minIdx = Array.IndexOf(n, n.Min());
    if (mid > s + 0.2 && mid > e + 0.2 && mid > 0.6) clusters["smooth_parabolic"].Add(item);
    else if (n.Max() > 0.8 && maxIdx <= L / 3) clusters["sharp_spike"].Add(item);
    else if (n.Min() < 0.2 && minIdx >= L / 3 && minIdx <= 2 * L / 3) clusters["v_reversal"].Add(item);
    else clusters["linear_grind"].Add(item);
}

var csvC = new StringBuilder();
csvC.AppendLine("timestamp,raw_mean,raw_0,raw_1,raw_2,raw_3,raw_4,raw_5,raw_6,raw_7,raw_8,raw_9,norm_0,norm_1,norm_2,norm_3,norm_4,norm_5,norm_6,norm_7,norm_8,norm_9,group");
foreach (var kv in clusters)
foreach (var item in kv.Value)
{
    string raw = string.Join(",", item.raw.Select(v => v.ToString("F2")));
    string nrm = string.Join(",", item.norm.Select(v => v.ToString("F4")));
    csvC.AppendLine($"{item.ts},{item.raw.Average():F2},{raw},{nrm},{kv.Key}");
}
File.WriteAllText("/tmp/sweep_candidates.csv", csvC.ToString());

var csvP = new StringBuilder();
csvP.AppendLine("name,avg_0,avg_1,avg_2,avg_3,avg_4,avg_5,avg_6,avg_7,avg_8,avg_9,count");
foreach (var kv in clusters)
{
    if (kv.Value.Count == 0) continue;
    int mL = kv.Value.Max(v => v.norm.Length);
    double[] avg = new double[mL];
    for (int i = 0; i < mL; i++) avg[i] = kv.Value.Where(v => v.norm.Length > i).Average(v => v.norm[i]);
    csvP.AppendLine($"{kv.Key},{string.Join(",", avg.Select(a => a.ToString("F4")))},{kv.Value.Count}");
}
File.WriteAllText("/tmp/sweep_prototypes.csv", csvP.ToString());

Console.WriteLine();
foreach (var kv in clusters)
{
    Console.WriteLine($"\n=== {kv.Key.ToUpper()} ({kv.Value.Count}) ===");
    foreach (var item in kv.Value.Take(5))
    {
        string shape = string.Join(" ", item.norm.Select(p => $"{p:F2}"));
        Console.WriteLine($"  ts={item.ts} px={item.raw[item.raw.Length/2]:F0} [{shape}]");
    }
}
Console.WriteLine($"\nSaved sweep_candidates.csv + sweep_prototypes.csv");