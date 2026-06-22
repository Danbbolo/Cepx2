using CEPx.Core;
using CEPx.Pipeline;

Console.WriteLine("Fetching BTCUSDT 1m candles...");
MarketEvent[] ticks;
try { ticks = PipelineFunctions.FetchBinanceHistorical("BTCUSDT", "1m", 100); }
catch (Exception ex) { Console.WriteLine($"API failed: {ex.Message}. Using synthetic."); ticks = PipelineFunctions.SyntheticTicks("BTCUSDT"); }

Console.WriteLine($"Loaded {ticks.Length} ticks. Running pipeline...");

var window = new List<MarketEvent>();
var similarities = new List<double>();
var sweepSims = new List<double>();

foreach (var tick in ticks)
{
    window.Add(tick);
    if (window.Count < 5) continue;

    var w = window.TakeLast(5).ToArray();
    var sweep = PipelineFunctions.DetectSweepStart(w);
    var full = window.ToArray();
    var score = PipelineFunctions.ScoreEvent(new CepEvent(tick.Timestamp, "BTCUSDT", "SweepStart", tick.Price, ""), full);

    if (window.Count >= 10 && score.PatternSimilarity > 0)
        similarities.Add(score.PatternSimilarity);

    if (sweep.HasValue)
    {
        var evtScore = PipelineFunctions.ScoreEvent(sweep.Value, full);
        if (evtScore.PatternSimilarity > 0)
            sweepSims.Add(evtScore.PatternSimilarity);
    }
}

Console.WriteLine();
Console.WriteLine("=== SIMILARITY REPORT ===");
if (similarities.Count > 0)
{
    Console.WriteLine($"Samples with DTW: {similarities.Count}");
    Console.WriteLine($"Max similarity: {similarities.Max():F4}");
    Console.WriteLine($"Min similarity: {similarities.Min():F4}");
    Console.WriteLine($"Avg similarity: {similarities.Average():F4}");

    var p50 = similarities.OrderBy(s => s).ElementAt(similarities.Count / 2);
    var p75 = similarities.OrderBy(s => s).ElementAt(similarities.Count * 3 / 4);
    Console.WriteLine($"Median (p50): {p50:F4}");
    Console.WriteLine($"p75: {p75:F4}");
    Console.WriteLine($"Recommended threshold: {p50:F2}");
}
else
    Console.WriteLine("No DTW samples (need >= 10 ticks).");

if (sweepSims.Count > 0)
    Console.WriteLine($"Sweep similarity avg: {sweepSims.Average():F4} (n={sweepSims.Count})");