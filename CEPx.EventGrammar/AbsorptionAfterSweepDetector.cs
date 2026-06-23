using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

public class AbsorptionAfterSweepDetector
{
    private readonly EventGrammarConfig _config;

    public AbsorptionAfterSweepDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Detects absorption after a sweep: high volume with minimal price continuation.
    /// Trapped aggressive orders being absorbed = potential reversal signal.
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

    private CepEvent? CheckAbsorption(IList<MarketEvent> window, CepEvent sweep)
    {
        if (window.Count < 2) return null;

        // Compute average volume of all but the last tick
        double avgVol = 0;
        for (int i = 0; i < window.Count - 1; i++)
            avgVol += window[i].Volume;
        avgVol /= (window.Count - 1);

        var last = window[window.Count - 1];

        // High volume on the last tick?
        if (last.Volume <= avgVol * _config.AbsorptionVolumeMultiplier)
            return null;

        // Minimal price movement?
        double pctMove = Math.Abs(last.Price - sweep.Price) / sweep.Price * 100;
        if (pctMove >= _config.SweepPctThreshold)
            return null; // price moved too much — not absorption

        return new CepEvent(last.Timestamp, last.Symbol, "AbsorptionAfterSweep", last.Price, sweep.Context);
    }
}
