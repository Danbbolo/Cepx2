using CEPx.Core;
using CEPx.Pipeline;

long tStart = 1712880000000; long tEnd = 1713052740000;
var ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000, tStart, tEnd);
if (ticks.Length < 100) ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 1000);

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

var allNorm = new List<(long ts, double[] norm, double[] raw, int sweepIdx)>();
for (int i = 0; i < rawWindows.Count; i++)
{
    var (ts, prices) = rawWindows[i];
    double mn = prices.Min(), mx = prices.Max(), r = mx - mn;
    if (r == 0) continue;
    allNorm.Add((ts, prices.Select(v => (v - mn) / r).ToArray(), prices, i));
}

var spikes = new List<(long ts, double[] norm, double[] raw)>();
foreach (var item in allNorm)
{
    var n = item.norm; int L = n.Length; if (L < 5) continue;
    int maxIdx = Array.IndexOf(n, n.Max());
    if (n.Max() > 0.8 && maxIdx <= L / 3)
        spikes.Add((item.ts, n, item.raw));
}

Console.WriteLine("=== TOP 5 SHARP_SPIKE CANDIDATES ===\n");
int num = 1;
foreach (var sp in spikes.Take(5))
{
    var dt = DateTimeOffset.FromUnixTimeMilliseconds(sp.ts).UtcDateTime;
    double sweepPx = sp.raw[sp.raw.Length / 2];
    string shape = string.Join(", ", sp.norm.Select(v => v.ToString("F2")));

    double start = sp.norm[0], peak = sp.norm.Max();
    int peakIdx = Array.IndexOf(sp.norm, peak);
    double retrace = sp.norm[^1] - peak;
    string dir = sp.raw[peakIdx] > sp.raw[0] ? "up" : "down";
    string speed = Math.Abs(retrace) > 0.5 ? "fast" : "slow";
    string desc = $"Rapid move {dir} peaking at candle {peakIdx+1}, {speed} retrace over {sp.norm.Length - peakIdx - 1} candles";

    Console.WriteLine($"{num}. Timestamp: {dt:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"   Price: {sweepPx:F0}");
    Console.WriteLine($"   Shape: [{shape}]");
    Console.WriteLine($"   Description: {desc}\n");
    num++;
}