using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects double top and double bottom formations.
/// A double top: two swing highs at approximately the same level (within 0.1%),
/// separated by ≥5 ticks, with price now below the interim low.
/// A double bottom: two swing lows at approximately the same level.
///
/// Scoring (0.0–1.0):
///   A. Price equality — how close are the two tops/bottoms? (exact → 1.0)
///   B. Time separation — more ticks between them = more credible (≥10 → 1.0)
///   C. Reversal confirmation — has price broken the interim level? (yes → 1.0)
///
/// Output: CepEvent with Type "DoubleTop" or "DoubleBottom", score in Context.
/// </summary>
public class DoubleStructureDetector
{
    private const double PRICE_TOLERANCE_PCT = 0.001; // 0.1% tolerance
    private const int MIN_SEPARATION_TICKS = 5;

    /// <summary>
    /// Detect double top / double bottom using swing tracker state and price window.
    /// </summary>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        double swingHigh, double swingLow,
        double currentSwingRange,
        long swingHighTimestamp, long swingLowTimestamp,
        int ticksSinceLastSwing,
        EventGrammarConfig? config = null)
    {
        int n = priceWindow.Length;
        if (n < 5) return null;
        if (currentSwingRange <= 0) return null;

        double currentPrice = priceWindow[n - 1].Price;
        double midPrice = (swingHigh + swingLow) / 2;

        // ── Detect Double Top ─────────────────────────────────────
        // Price approached the prior swing high again and is now below it
        bool nearSwingHigh = Math.Abs(currentPrice - swingHigh) / swingHigh < PRICE_TOLERANCE_PCT;
        bool belowSwingHigh = currentPrice < swingHigh * (1 - PRICE_TOLERANCE_PCT);

        if (belowSwingHigh && swingHighTimestamp > 0 && ticksSinceLastSwing >= MIN_SEPARATION_TICKS)
        {
            // Check if price recently visited near the swing high
            double maxRecent = double.MinValue;
            for (int i = 0; i < n; i++)
                if (priceWindow[i].Price > maxRecent) maxRecent = priceWindow[i].Price;

            bool recentHighNearSwing = Math.Abs(maxRecent - swingHigh) / swingHigh < PRICE_TOLERANCE_PCT;
            if (recentHighNearSwing && maxRecent > currentPrice)
            {
                double equalityScore = Clamp01(1.0 - (Math.Abs(maxRecent - swingHigh) / swingHigh / PRICE_TOLERANCE_PCT));
                double separationScore = Clamp01(ticksSinceLastSwing / 10.0);
                double reversalScore = Clamp01((swingHigh - currentPrice) / (currentSwingRange * 0.5));

                double score = Clamp01(equalityScore * 0.40 + separationScore * 0.30 + reversalScore * 0.30);
                if (score > 0.4)
                {
                    var last = priceWindow[n - 1];
                    string ctx = $"score:{score:F2}:level={swingHigh:F2}:sep={ticksSinceLastSwing}";
                    return new CepEvent(last.Timestamp, last.Symbol, "DoubleTop", last.Price, ctx);
                }
            }
        }

        // ── Detect Double Bottom ──────────────────────────────────
        bool nearSwingLow = Math.Abs(currentPrice - swingLow) / swingLow < PRICE_TOLERANCE_PCT;
        bool aboveSwingLow = currentPrice > swingLow * (1 + PRICE_TOLERANCE_PCT);

        if (aboveSwingLow && swingLowTimestamp > 0 && ticksSinceLastSwing >= MIN_SEPARATION_TICKS)
        {
            double minRecent = double.MaxValue;
            for (int i = 0; i < n; i++)
                if (priceWindow[i].Price < minRecent) minRecent = priceWindow[i].Price;

            bool recentLowNearSwing = Math.Abs(minRecent - swingLow) / swingLow < PRICE_TOLERANCE_PCT;
            if (recentLowNearSwing && minRecent < currentPrice)
            {
                double equalityScore = Clamp01(1.0 - (Math.Abs(minRecent - swingLow) / swingLow / PRICE_TOLERANCE_PCT));
                double separationScore = Clamp01(ticksSinceLastSwing / 10.0);
                double reversalScore = Clamp01((currentPrice - swingLow) / (currentSwingRange * 0.5));

                double score = Clamp01(equalityScore * 0.40 + separationScore * 0.30 + reversalScore * 0.30);
                if (score > 0.4)
                {
                    var last = priceWindow[n - 1];
                    string ctx = $"score:{score:F2}:level={swingLow:F2}:sep={ticksSinceLastSwing}";
                    return new CepEvent(last.Timestamp, last.Symbol, "DoubleBottom", last.Price, ctx);
                }
            }
        }

        return null;
    }

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1.0, v));
}
