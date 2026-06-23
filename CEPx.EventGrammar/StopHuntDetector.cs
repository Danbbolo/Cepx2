using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects engineered stop hunts: a brief break of a swing level followed by
/// an immediate reversal with volume expansion. The classic "liquidity grab"
/// where stops above a swing high or below a swing low are triggered, then
/// price reverses — trapping breakout traders.
///
/// Scoring (0.0–1.0):
///   A. Penetration depth — how far beyond the swing level? (0.05%→0.5, 0.2%+→1.0)
///   B. Reversal speed — how fast did price reverse? (1 tick → 1.0, 5+→0.1)
///   C. Volume on reversal — elevated volume confirms the trap (1.5× → 1.0)
///   D. BOS context — was a BOS just detected? (BOS + reversal = CHoCH → bonus)
///
/// Output: CepEvent with Type "StopHunt", direction and score in Context.
/// </summary>
public class StopHuntDetector
{
    /// <summary>
    /// Detect a stop hunt using swing levels, BOS state, volume context, and price window.
    /// </summary>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        double swingHigh, double swingLow,
        bool bullishBOS, bool bearishBOS,
        double bosPrice, long bosTimestamp,
        bool bullishCHoCH, bool bearishCHoCH,
        bool isVolumeExpanding,
        EventGrammarConfig? config = null)
    {
        int n = priceWindow.Length;
        if (n < 4) return null;

        double currentPrice = priceWindow[n - 1].Price;
        double prevPrice = priceWindow[n - 2].Price;

        // ── Check for stop hunt ABOVE swing high (bullish trap → bearish reversal) ──
        if (swingHigh > 0)
        {
            // Did price recently break above swing high and now is below it?
            double maxInWindow = double.MinValue;
            int maxIdx = 0;
            for (int i = 0; i < n; i++)
            {
                if (priceWindow[i].Price > maxInWindow)
                { maxInWindow = priceWindow[i].Price; maxIdx = i; }
            }

            double penetrationPct = (maxInWindow - swingHigh) / swingHigh * 100;
            bool brokeAbove = penetrationPct > 0.02; // broke by at least 0.02%
            bool nowBelowSwing = currentPrice < swingHigh;
            bool recentBOS = bullishBOS && Math.Abs(maxInWindow - bosPrice) / bosPrice < 0.001;

            if (brokeAbove && nowBelowSwing && maxIdx < n - 1)
            {
                // ── A. Penetration depth ──────────────────────────
                double depthScore = Clamp01(penetrationPct / 0.2);

                // ── B. Reversal speed ─────────────────────────────
                int reversalTicks = n - 1 - maxIdx;
                double speedScore = reversalTicks <= 1 ? 1.0
                                  : reversalTicks <= 2 ? 0.8
                                  : reversalTicks <= 3 ? 0.5
                                  : 0.2;

                // ── C. Volume on reversal ─────────────────────────
                double reversalVol = 0;
                for (int i = maxIdx + 1; i < n; i++)
                    reversalVol += priceWindow[i].Volume;
                double avgVol = 0;
                for (int i = 0; i < n; i++) avgVol += priceWindow[i].Volume;
                avgVol /= n;
                double volRatio = avgVol > 0 ? (reversalVol / (n - 1 - maxIdx)) / avgVol : 1.0;
                double volScore = isVolumeExpanding ? 1.0 : Clamp01(volRatio / 1.5);

                // ── D. BOS context bonus ──────────────────────────
                double bosBonus = recentBOS ? 0.15 : 0.0;
                if (bullishCHoCH) bosBonus += 0.10; // CHoCH makes it stronger

                double score = Clamp01(depthScore * 0.30 + speedScore * 0.30
                                     + volScore * 0.25 + bosBonus);
                if (score > 0.4)
                {
                    var last = priceWindow[n - 1];
                    string ctx = $"score:{score:F2}:dir=bearish:pen={penetrationPct:F3}%:vol={volRatio:F1}";
                    return new CepEvent(last.Timestamp, last.Symbol, "StopHunt", last.Price, ctx);
                }
            }
        }

        // ── Check for stop hunt BELOW swing low (bearish trap → bullish reversal) ──
        if (swingLow > 0)
        {
            double minInWindow = double.MaxValue;
            int minIdx = 0;
            for (int i = 0; i < n; i++)
            {
                if (priceWindow[i].Price < minInWindow)
                { minInWindow = priceWindow[i].Price; minIdx = i; }
            }

            double penetrationPct = (swingLow - minInWindow) / swingLow * 100;
            bool brokeBelow = penetrationPct > 0.02;
            bool nowAboveSwing = currentPrice > swingLow;
            bool recentBOS = bearishBOS && Math.Abs(minInWindow - bosPrice) / bosPrice < 0.001;

            if (brokeBelow && nowAboveSwing && minIdx < n - 1)
            {
                double depthScore = Clamp01(penetrationPct / 0.2);

                int reversalTicks = n - 1 - minIdx;
                double speedScore = reversalTicks <= 1 ? 1.0
                                  : reversalTicks <= 2 ? 0.8
                                  : reversalTicks <= 3 ? 0.5
                                  : 0.2;

                double reversalVol = 0;
                for (int i = minIdx + 1; i < n; i++)
                    reversalVol += priceWindow[i].Volume;
                double avgVol = 0;
                for (int i = 0; i < n; i++) avgVol += priceWindow[i].Volume;
                avgVol /= n;
                double volRatio = avgVol > 0 ? (reversalVol / (n - 1 - minIdx)) / avgVol : 1.0;
                double volScore = isVolumeExpanding ? 1.0 : Clamp01(volRatio / 1.5);

                double bosBonus = recentBOS ? 0.15 : 0.0;
                if (bearishCHoCH) bosBonus += 0.10;

                double score = Clamp01(depthScore * 0.30 + speedScore * 0.30
                                     + volScore * 0.25 + bosBonus);
                if (score > 0.4)
                {
                    var last = priceWindow[n - 1];
                    string ctx = $"score:{score:F2}:dir=bullish:pen={penetrationPct:F3}%:vol={volRatio:F1}";
                    return new CepEvent(last.Timestamp, last.Symbol, "StopHunt", last.Price, ctx);
                }
            }
        }

        return null;
    }

    private static double Clamp01(double v) => Math.Max(0, Math.Min(1.0, v));
}
