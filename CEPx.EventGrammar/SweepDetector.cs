using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

public class SweepDetector
{
    private readonly EventGrammarConfig _config;

    public SweepDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    public IObservable<CepEvent> Detect(IObservable<MarketEvent> ticks)
    {
        return ticks
            .Buffer(_config.SweepWindowTicks, 1) // sliding window
            .Where(window => window.Count >= _config.SweepWindowTicks)
            .Select(window => CheckSweep(window))
            .Where(evt => evt != null)
            .Select(evt => evt!.Value);
    }

    private CepEvent? CheckSweep(IList<MarketEvent> window)
    {
        if (window.Count < _config.SweepWindowTicks) return null;

        var prices = new double[_config.SweepWindowTicks];
        for (int i = 0; i < _config.SweepWindowTicks; i++)
            prices[i] = window[i].Price;

        double high = prices.Max();
        double low = prices.Min();
        double avg = prices.Average();
        if (avg <= 0) return null;

        double pct = (high - low) / avg * 100;
        if (pct < _config.SweepPctThreshold) return null;

        var last = window[window.Count - 1];
        string dir = prices[_config.SweepWindowTicks - 1] > prices[0] ? "bullish" : "bearish";
        return new CepEvent(last.Timestamp, last.Symbol, "SweepStart", last.Price, dir);
    }
}
