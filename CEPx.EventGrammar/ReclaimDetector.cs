using System.Reactive.Linq;
using CEPx.Core;

namespace CEPx.EventGrammar;

public class ReclaimDetector
{
    private readonly EventGrammarConfig _config;

    public ReclaimDetector(EventGrammarConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Detects reclaims: when price crosses back past the sweep origin
    /// within a configurable window after a sweep is detected.
    /// </summary>
    public IObservable<CepEvent> Detect(IObservable<CepEvent> sweeps, IObservable<MarketEvent> ticks)
    {
        return sweeps
            .Where(s => s.Type == "SweepStart")
            .SelectMany(sweep =>
                ticks
                    .Take(_config.ReclaimWindow)
                    .TakeUntil(tick => IsReclaim(tick, sweep))
                    .Where(tick => IsReclaim(tick, sweep))
                    .Take(1)
                    .Select(tick => new CepEvent(
                        tick.Timestamp,
                        tick.Symbol,
                        "Reclaim",
                        tick.Price,
                        sweep.Context))
            );
    }

    private static bool IsReclaim(MarketEvent tick, CepEvent sweep)
    {
        bool isBullish = sweep.Context == "bullish";
        return isBullish
            ? tick.Price < sweep.Price  // bullish sweep: price drops below origin → reclaim
            : tick.Price > sweep.Price; // bearish sweep: price rises above origin → reclaim
    }
}
