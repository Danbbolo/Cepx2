using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects exhaustion after a sweep: momentum deceleration.
/// Price range in second half of window is much smaller than first half —
/// the aggressive move is running out of steam = potential reversal signal.
/// 
/// Scoring (0.0–1.0) — four dimensions, configurable via EventGrammarConfig:
///   A. Deceleration ratio — how much did range shrink? (lower = stronger exhaustion)
///   B. Initial move magnitude — bigger initial impulse = more significant exhaustion
///   C. Reversal direction — does final price lean against the sweep?
///   D. Time progression — exhaustion in later ticks is more credible
/// </summary>
public class ExhaustionDetector
{
    private readonly EventGrammarConfig _config;

    public ExhaustionDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Detect exhaustion events from a stream of sweeps and ticks.
    /// </summary>
    public IObservable<CepEvent> Detect(IObservable<CepEvent> sweeps, IObservable<MarketEvent> ticks)
    {
        return sweeps
            .Where(s => s.Type == "SweepStart")
            .SelectMany(sweep =>
                ticks
                    .Take(_config.ExhaustionWindow)
                    .Buffer(_config.ExhaustionWindow)
                    .Where(window => window.Count >= 4)
                    .Select(window => CheckExhaustion(window, sweep))
                    .Where(evt => evt != null)
                    .Select(evt => evt!.Value)
                    .Take(1)
            );
    }

    /// <summary>
    /// Static convenience method — same logic, usable without Rx streams.
    /// </summary>
    public static CepEvent? DetectStatic(IList<MarketEvent> window, CepEvent sweep, EventGrammarConfig? config = null)
    {
        var cfg = config ?? new EventGrammarConfig();
        var detector = new ExhaustionDetector(cfg);
        return detector.CheckExhaustion(window, sweep);
    }

    private CepEvent? CheckExhaustion(IList<MarketEvent> window, CepEvent sweep)
    {
        int n = window.Count;
        if (n < 4) return null;

        int half = n / 2;

        // ── Pass 1: compute halves ───────────────────────────────
        double firstMin = double.MaxValue, firstMax = double.MinValue;
        for (int i = 0; i < half; i++)
        {
            if (window[i].Price < firstMin) firstMin = window[i].Price;
            if (window[i].Price > firstMax) firstMax = window[i].Price;
        }
        double firstRange = (firstMax - firstMin) / sweep.Price * 100;

        double secondMin = double.MaxValue, secondMax = double.MinValue;
        for (int i = half; i < n; i++)
        {
            if (window[i].Price < secondMin) secondMin = window[i].Price;
            if (window[i].Price > secondMax) secondMax = window[i].Price;
        }
        double secondRange = (secondMax - secondMin) / sweep.Price * 100;

        // ── Gate: hard thresholds ─────────────────────────────────
        if (firstRange <= 0) return null;
        double ratio = secondRange / firstRange;
        if (ratio >= _config.ExhaustionStallRatio) return null;

        // ── Scoring ──────────────────────────────────────────────

        // A. Deceleration ratio: 0.0 (full stall)→1.0, threshold→0.0
        double stallNorm = _config.ExhaustionStallNorm;
        double decelScore = Clamp01(1.0 - (ratio / stallNorm));

        // B. Initial move magnitude: bigger impulse = stronger signal
        double moveNorm = _config.ExhaustionMoveNormPct;
        double magScore = Clamp01(firstRange / moveNorm);

        // C. Reversal direction: does the last tick lean against the sweep?
        bool isBullish = sweep.Context == "bullish";
        double firstPrice = window[0].Price;
        double lastPrice = window[n - 1].Price;
        double reversalScore = ScoreReversalLean(firstPrice, lastPrice, isBullish);

        // D. Time progression: exhaustion credibility increases with more ticks
        double progressScore = Clamp01((double)n / _config.ExhaustionWindow);

        // ── Combined score ───────────────────────────────────────
        // Weights: deceleration 45%, magnitude 20%, reversal lean 25%, progression 10%
        double score = Clamp01(
            decelScore * 0.45 + magScore * 0.20 + reversalScore * 0.25 + progressScore * 0.10);

        string context = $"score:{score:F2}:ratio={ratio:F3}:firstRange={firstRange:F3}%:revLean={reversalScore:F2}";
        return new CepEvent(window[n - 1].Timestamp, window[n - 1].Symbol,
            "ExhaustionAfterSweep", lastPrice, context);
    }

    // ── Scoring helpers ──────────────────────────────────────────────

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>
    /// Score reversal lean: how much did price move against the sweep direction?
    /// Returns 0.3 (continued with sweep) to 1.0 (strong reversal).
    /// </summary>
    private static double ScoreReversalLean(double firstPrice, double lastPrice, bool isBullishSweep)
    {
        double pctChange = (lastPrice - firstPrice) / firstPrice * 100;

        if (isBullishSweep)
        {
            // Bullish sweep: price dropping = reversal (positive for exhaustion)
            if (pctChange <= -0.2) return 1.0;  // strong reversal
            if (pctChange <= -0.1) return 0.8;
            if (pctChange <= 0.0) return 0.6;   // flat = mild exhaustion
            if (pctChange <= 0.1) return 0.4;   // still rising = weak exhaustion
            return 0.3;                          // continuing strongly
        }
        else
        {
            // Bearish sweep: price rising = reversal
            if (pctChange >= 0.2) return 1.0;
            if (pctChange >= 0.1) return 0.8;
            if (pctChange >= 0.0) return 0.6;
            if (pctChange >= -0.1) return 0.4;
            return 0.3;
        }
    }
}
