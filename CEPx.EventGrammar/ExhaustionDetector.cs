using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

public class ExhaustionDetector
{
    private readonly EventGrammarConfig _config;

    public ExhaustionDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Detects exhaustion after a sweep: momentum deceleration.
    /// Price range in second half of window is much smaller than first half.
    /// </summary>
    public IObservable<CepEvent> Detect(IObservable<CepEvent> sweeps, IObservable<MarketEvent> ticks)
    {
        return sweeps
            .Where(s => s.Type == "SweepStart")
            .SelectMany(sweep =>
                ticks
                    .Take(_config.ExhaustionWindow)
                    .Buffer(_config.ExhaustionWindow)
                    .Where(window => window.Count >= 2)
                    .Select(window => CheckExhaustion(window, sweep))
                    .Where(evt => evt != null)
                    .Select(evt => evt!.Value)
                    .Take(1)
            );
    }

    private CepEvent? CheckExhaustion(IList<MarketEvent> window, CepEvent sweep)
    {
        int n = window.Count;
        if (n < 4) return null;

        int half = n / 2;

        // First half price range
        double firstMin = double.MaxValue, firstMax = double.MinValue;
        for (int i = 0; i < half; i++)
        {
            if (window[i].Price < firstMin) firstMin = window[i].Price;
            if (window[i].Price > firstMax) firstMax = window[i].Price;
        }
        double firstRange = (firstMax - firstMin) / sweep.Price * 100;

        // Second half price range
        double secondMin = double.MaxValue, secondMax = double.MinValue;
        for (int i = half; i < n; i++)
        {
            if (window[i].Price < secondMin) secondMin = window[i].Price;
            if (window[i].Price > secondMax) secondMax = window[i].Price;
        }
        double secondRange = (secondMax - secondMin) / sweep.Price * 100;

        // Exhaustion: second half range is much smaller than first half
        if (firstRange <= 0) return null;
        double ratio = secondRange / firstRange;

        if (ratio < _config.ExhaustionStallRatio)
        {
            var last = window[n - 1];
            return new CepEvent(last.Timestamp, last.Symbol, "ExhaustionAfterSweep", last.Price, sweep.Context);
        }

        return null;
    }
}
