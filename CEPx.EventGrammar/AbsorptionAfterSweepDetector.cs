using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects absorption after a sweep: high volume with minimal price continuation.
/// Trapped aggressive orders being absorbed = potential reversal signal.
/// 
/// Scoring (0.0–1.0) — three dimensions, configurable via EventGrammarConfig:
///   A. Volume intensity — how many × average is the spike? (3x→0.0, 8x→1.0)
///   B. Price stability — how little did price move during the spike? (0%→1.0, max%→0.0)
///   C. Volume concentration — is volume concentrated in one tick vs spread across window?
/// </summary>
public class AbsorptionAfterSweepDetector
{
    private readonly EventGrammarConfig _config;

    public AbsorptionAfterSweepDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Detect absorption events from a stream of sweeps and ticks.
    /// </summary>
    public IObservable<CepEvent> Detect(IObservable<CepEvent> sweeps, IObservable<MarketEvent> ticks)
    {
        return sweeps
            .Where(s => s.Type == "SweepStart")
            .SelectMany(sweep =>
                ticks
                    .Take(_config.AbsorptionWindow)
                    .Buffer(_config.AbsorptionWindow)
                    .Where(window => window.Count >= 2)
                    .Select(window => CheckAbsorption(window, sweep))
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
        var detector = new AbsorptionAfterSweepDetector(cfg);
        return detector.CheckAbsorption(window, sweep);
    }

    private CepEvent? CheckAbsorption(IList<MarketEvent> window, CepEvent sweep)
    {
        int n = window.Count;
        if (n < 2) return null;

        // ── Pass 1: collect stats ────────────────────────────────
        double avgVol = 0;
        double maxVol = 0;
        int maxVolIndex = 0;
        for (int i = 0; i < n - 1; i++)
        {
            avgVol += window[i].Volume;
            if (window[i].Volume > maxVol) { maxVol = window[i].Volume; maxVolIndex = i; }
        }
        avgVol /= (n - 1);

        var last = window[n - 1];
        if (last.Volume > maxVol) { maxVol = last.Volume; maxVolIndex = n - 1; }

        double volRatio = last.Volume / Math.Max(avgVol, 1e-10);

        // ── Gate: hard thresholds ─────────────────────────────────
        if (volRatio < _config.AbsorptionVolumeMultiplier) return null;

        double pctMove = Math.Abs(last.Price - sweep.Price) / sweep.Price * 100;
        if (pctMove >= _config.AbsorptionMaxPriceMovePct) return null;

        // ── Scoring ──────────────────────────────────────────────

        // A. Volume intensity: 3x→0.0, 5.5x→0.5, 8x+→1.0
        double volNorm = _config.AbsorptionVolumeNorm;
        double volMin = _config.AbsorptionVolumeMultiplier;
        double volScore = Clamp01((volRatio - volMin) / (volNorm - volMin));

        // B. Price stability: 0%→1.0, max%→0.0
        double maxMove = _config.AbsorptionMaxPriceMovePct;
        double stabilityScore = Clamp01(1.0 - (pctMove / maxMove));

        // C. Volume concentration: how dominant is the spike vs rest of window?
        // totalVol = sum of all ticks; concentration = spikeVol / totalVol
        double totalVol = avgVol * (n - 1) + last.Volume;
        double concentration = totalVol > 0 ? last.Volume / totalVol : 0;
        double expectedShare = 1.0 / n; // if uniform, each tick is 1/n of total
        double concentrationScore = Clamp01((concentration - expectedShare) / (1.0 - expectedShare));

        // ── Combined score ───────────────────────────────────────
        // Weights: volume 40%, stability 35%, concentration 25%
        double score = Clamp01(volScore * 0.40 + stabilityScore * 0.35 + concentrationScore * 0.25);

        string context = $"score:{score:F2}:volRatio={volRatio:F1}:move={pctMove:F3}%";
        return new CepEvent(last.Timestamp, last.Symbol, "AbsorptionAfterSweep", last.Price, context);
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
