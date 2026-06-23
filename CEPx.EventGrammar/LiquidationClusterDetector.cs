using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects liquidation clusters against a sweep direction.
/// Long liquidations (SELL) against a bullish sweep = trapped longs being stopped out = reversal signal.
/// Short liquidations (BUY) against a bearish sweep = trapped shorts being stopped out = reversal signal.
/// 
/// Scoring (0.0–1.0) combines:
///   - Count score: min(count / 8, 1.0) — more liquidations = stronger
///   - Quantity score: min(totalQty / 50, 1.0) — larger notional = stronger
///   - Time concentration: higher when liquidations cluster in a tight window
///   - Direction ratio: what % of ALL liquidations in window are against sweep (high = cascade)
///   - Proximity bonus: liquidations near sweep extreme get a bonus
/// </summary>
public class LiquidationClusterDetector
{
    private const int MIN_COUNT = 3;
    private const double MIN_TOTAL_QTY = 10.0;
    private const double PROXIMITY_PCT = 0.003; // 0.3% of sweep origin = "near extreme"

    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        LiquidationEvent[] liquidations,
        double sweepOriginPrice,
        bool isBullishSweep,
        long windowStartMs,
        long windowEndMs)
    {
        // Count liquidations within the time window, collect timestamps and prices
        int count = 0;
        int totalInWindow = 0; // ALL liquidations in window (both directions)
        double totalQty = 0;
        var liqTimestamps = new List<long>();
        int nearSweepCount = 0;

        foreach (var liq in liquidations)
        {
            if (liq.Timestamp >= windowStartMs && liq.Timestamp <= windowEndMs)
            {
                totalInWindow++;
                bool isAgainstSweep = isBullishSweep
                    ? liq.IsLongLiquidation   // longs liquidated against bullish = reversal
                    : liq.IsShortLiquidation; // shorts liquidated against bearish = reversal

                if (isAgainstSweep)
                {
                    count++;
                    totalQty += liq.Quantity;
                    liqTimestamps.Add(liq.Timestamp);

                    // Check proximity to sweep origin (within 0.3% of the extreme)
                    double distFromSweep = Math.Abs(liq.Price - sweepOriginPrice) / sweepOriginPrice;
                    if (distFromSweep <= PROXIMITY_PCT)
                        nearSweepCount++;
                }
            }
        }

        // DEBUG_LIQ: Log every detection attempt when liquidations exist in window
        if (totalInWindow > 0)
        {
            string sweepDir = isBullishSweep ? "bullish" : "bearish";
            Console.WriteLine($"[DEBUG_LIQ] Detect window ({sweepDir} sweep @ {sweepOriginPrice:F2}): " +
                $"{totalInWindow} liqs in window, {count} against sweep, totalQty={totalQty:F1}, nearSweep={nearSweepCount}");
        }
        // END DEBUG_LIQ

        // Threshold: at least 3 liquidations against sweep within window
        if (count >= MIN_COUNT && totalQty >= MIN_TOTAL_QTY)
        {
            // ── Scoring ──────────────────────────────────────────
            // Count score: 3→0.375, 5→0.625, 8+→1.0
            double countScore = Math.Min(count / 8.0, 1.0);

            // Quantity score: 10→0.2, 25→0.5, 50+→1.0
            double qtyScore = Math.Min(totalQty / 50.0, 1.0);

            // Time concentration: ratio of actual span to window span (lower = more clustered)
            double timeScore = 0.5;
            if (liqTimestamps.Count >= 2)
            {
                long actualSpan = liqTimestamps.Max() - liqTimestamps.Min();
                long windowSpan = windowEndMs - windowStartMs;
                if (windowSpan > 0)
                {
                    double concentration = 1.0 - Math.Min((double)actualSpan / windowSpan, 1.0);
                    timeScore = 0.3 + concentration * 0.7; // range 0.3–1.0
                }
            }

            // Direction ratio: what % of all liqs in window are against sweep
            // High ratio (e.g. 80%+) = cascade liquidation, very strong signal
            double dirRatio = totalInWindow > 0 ? (double)count / totalInWindow : 0;
            double dirScore = Math.Min(dirRatio / 0.7, 1.0); // 70%+ = full score

            // Proximity bonus: up to 0.2 extra if liquidations are near sweep origin
            double proximityBonus = Math.Min(nearSweepCount / (double)count, 1.0) * 0.2;

            // Combined score: weighted average + proximity bonus
            double score = Math.Min(
                (countScore * 0.25) + (qtyScore * 0.20) + (timeScore * 0.25) + (dirScore * 0.30) + proximityBonus,
                1.0);

            var lastTick = priceWindow[priceWindow.Length - 1];
            string dir = isBullishSweep ? "longs_stopped" : "shorts_stopped";
            string context = $"{dir}:{score:F2}:{count}:{totalQty:F1}";

            // DEBUG_LIQ: Log cluster detection with full scoring breakdown
            Console.WriteLine($"[DEBUG_LIQ] ★ LIQ CLUSTER FIRED {dir} score={score:F2} " +
                $"(count={countScore:F2} qty={qtyScore:F2} time={timeScore:F2} dirRatio={dirRatio:F2}→{dirScore:F2} prox+{proximityBonus:F2})");
            // END DEBUG_LIQ

            return new CepEvent(lastTick.Timestamp, lastTick.Symbol, "LiquidationCluster", lastTick.Price, context);
        }

        // DEBUG_LIQ: Log near-misses (liquidations exist but below threshold)
        if (count > 0 && (count < MIN_COUNT || totalQty < MIN_TOTAL_QTY))
        {
            Console.WriteLine($"[DEBUG_LIQ]   Near-miss: {count} against sweep (need {MIN_COUNT}), totalQty={totalQty:F1} (need {MIN_TOTAL_QTY})");
        }
        // END DEBUG_LIQ

        return null;
    }
}
