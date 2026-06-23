using CEPx.Core;

namespace CEPx.EventGrammar;

/// <summary>
/// Detects sustained directional momentum — the continuation signal.
/// A sweep that keeps moving in its original direction with consistent ticks
/// suggests the trend has legs, not an immediate reversal.
/// 
/// Scoring (0.0–1.0) — four dimensions, configurable via EventGrammarConfig:
///   A. Direction consistency — what % of ticks move in the dominant direction?
///   B. Net move magnitude — how far did price travel overall?
///   C. Velocity stability — is the movement smooth or erratic?
///   D. Kalman velocity alignment — does external Kalman estimate agree?
/// 
/// Output: CepEvent with Type "MomentumPersistence", score in Context.
/// </summary>
public class MomentumPersistenceDetector
{
    /// <summary>
    /// Detect sustained momentum in a price window.
    /// </summary>
    /// <param name="priceWindow">Recent price ticks (should match MomentumPersistenceWindowTicks in length).</param>
    /// <param name="kalmanVelocity">Optional Kalman velocity from ScoringEngine (0 = unavailable).</param>
    /// <param name="config">Optional config override.</param>
    /// <returns>CepEvent with score, or null if momentum is not sustained.</returns>
    public static CepEvent? Detect(
        MarketEvent[] priceWindow,
        double kalmanVelocity = 0.0,
        EventGrammarConfig? config = null)
    {
        var cfg = config ?? new EventGrammarConfig();
        int n = priceWindow.Length;
        int expected = cfg.MomentumPersistenceWindowTicks;

        // ── Use available ticks; scale thresholds for shorter windows ──
        double scale = Math.Min(1.0, (double)n / expected);

        // ── Pass 1: compute tick-to-tick returns and direction ──
        int upCount = 0, downCount = 0, flatCount = 0;
        var returns = new List<double>();
        double firstPrice = priceWindow[0].Price;
        double lastPrice = priceWindow[n - 1].Price;

        for (int i = 1; i < n; i++)
        {
            double ret = (priceWindow[i].Price - priceWindow[i - 1].Price) / priceWindow[i - 1].Price;
            returns.Add(ret);

            if (ret > 1e-8) upCount++;
            else if (ret < -1e-8) downCount++;
            else flatCount++;
        }

        if (returns.Count == 0) return null;

        // ── Gate: minimum directional movement ──
        double netMovePct = Math.Abs((lastPrice - firstPrice) / firstPrice * 100.0);
        double minMove = cfg.MomentumPersistenceMinNetMovePct * scale;
        if (netMovePct < minMove) return null;

        // Determine dominant direction
        bool isUp = upCount > downCount;
        int dominantCount = isUp ? upCount : downCount;
        double directionConsistency = (double)dominantCount / returns.Count;

        double minConsistency = cfg.MomentumPersistenceDirectionConsistencyMin;
        if (directionConsistency < minConsistency) return null;

        // ── Scoring ──────────────────────────────────────────────

        // A. Direction consistency: 0.6→0.0, 0.8→0.5, 1.0→1.0
        double consistencyScore = Clamp01((directionConsistency - 0.5) / 0.5);

        // B. Net move magnitude: 0.15%→0.3, 0.3%→0.6, 0.5%+→1.0
        double moveNorm = cfg.MomentumPersistenceMoveNormPct;
        double moveScore = Clamp01(netMovePct / moveNorm);

        // C. Velocity stability: how consistent are the tick-to-tick returns?
        // Lower std dev relative to mean absolute return = smoother momentum
        double meanAbsRet = returns.Average(r => Math.Abs(r));
        double stabilityScore = ScoreVelocityStability(returns, meanAbsRet);

        // D. Kalman velocity alignment: does external estimate agree with direction?
        double kalmanScore = 0.5; // neutral if unavailable
        if (Math.Abs(kalmanVelocity) > 1e-8)
        {
            bool kalmanAgrees = (isUp && kalmanVelocity > 0) || (!isUp && kalmanVelocity < 0);
            if (kalmanAgrees)
            {
                // Stronger Kalman velocity = higher confidence
                double kalmanStrength = Clamp01(Math.Abs(kalmanVelocity) / 2.0);
                kalmanScore = 0.5 + kalmanStrength * 0.5; // range 0.5–1.0
            }
            else
            {
                // Kalman disagrees — penalize
                kalmanScore = 0.2;
            }
        }

        // ── Combined score ───────────────────────────────────────
        // Weights: consistency 40%, move 30%, stability 20%, kalman 10%
        double score = Clamp01(
            consistencyScore * 0.40 + moveScore * 0.30 + stabilityScore * 0.20 + kalmanScore * 0.10);

        string dir = isUp ? "up" : "down";
        string context = $"score:{score:F2}:dir={dir}:consistency={directionConsistency:F2}:move={netMovePct:F2}%";

        return new CepEvent(priceWindow[n - 1].Timestamp, priceWindow[n - 1].Symbol,
            "MomentumPersistence", lastPrice, context);
    }

    // ── Scoring helpers ──────────────────────────────────────────────

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>
    /// Score velocity stability: returns 0.1 (extremely choppy) to 1.0 (perfectly smooth).
    /// Uses coefficient of variation: stdDev / meanAbsReturn.
    /// CV < 0.5 → score 1.0 (smooth), CV > 3.0 → score 0.1 (erratic).
    /// </summary>
    private static double ScoreVelocityStability(List<double> returns, double meanAbsRet)
    {
        if (meanAbsRet < 1e-10) return 0.5; // flat = neutral

        double variance = returns.Average(r => (r - returns.Average()) * (r - returns.Average()));
        double stdDev = Math.Sqrt(Math.Abs(variance));
        double cv = stdDev / meanAbsRet;

        // Map: cv=0.5→1.0, cv=1.5→0.5, cv=3.0+→0.1
        if (cv <= 0.5) return 1.0;
        if (cv >= 3.0) return 0.1;
        return 1.0 - (cv - 0.5) / (3.0 - 0.5) * 0.9;
    }
}
