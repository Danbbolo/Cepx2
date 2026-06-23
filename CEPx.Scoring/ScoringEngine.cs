using CEPx.Core;

namespace CEPx.Scoring;

public static class ScoringEngine
{
    private const double PROCESS_NOISE = 0.01;
    private const double MEASUREMENT_NOISE = 1.0;
    private const int WARPING_WINDOW = 3;

    private static readonly double[] SWEEP_PROTOTYPE =
        { 0.0, 0.05, 0.12, 0.21, 0.33, 0.48, 0.66, 0.87, 1.12, 1.40 };

    // Extracted from 30-day k-means (June 1-30, 2026) — best cluster centroid
    // C1 "steady fade": 667 continuation samples. Starts high, monotonic decline.
    private static readonly double[] CONTINUATION_PROTOTYPE =
        { 0.8481, 0.8262, 0.7799, 0.7187, 0.6482, 0.5822, 0.4553, 0.3214, 0.2099, 0.1606 };

    // Extracted from 5 user-labeled reversal timestamps (June 20-22, 2026)
    private static readonly double[] REVERSAL_PROTOTYPE =
        { 0.4434, 0.5098, 0.6322, 0.5679, 0.7094, 0.7853, 0.4302, 0.3838, 0.2818, 0.2207 };

    public static StructuralScore ScoreWithKalman(CepEvent evt, MarketEvent[] window)
    {
        if (window.Length == 0)
            return new StructuralScore(evt.Timestamp, evt.Symbol,
                0.0, 0.0, 0.0, 0.0, "sweep", 0.0, 0.0, 0.0);

        double price = window[0].Price;
        double velocity = 0.0;
        double p00 = 1.0, p01 = 0.0, p10 = 0.0, p11 = 1.0;
        const double dt = 1.0;

        for (int i = 0; i < window.Length; i++)
        {
            double z = window[i].Price;

            double xPred = price + dt * velocity;
            double vPred = velocity;
            double pp00 = p00 + dt * (p10 + p01) + dt * dt * p11 + PROCESS_NOISE;
            double pp01 = p01 + dt * p11;
            double pp10 = p10 + dt * p11;
            double pp11 = p11 + PROCESS_NOISE;

            double y = z - xPred;
            double s = pp00 + MEASUREMENT_NOISE;
            double k0 = pp00 / s;
            double k1 = pp10 / s;

            price = xPred + k0 * y;
            velocity = vPred + k1 * y;

            double new_p00 = (1.0 - k0) * pp00;
            double new_p01 = (1.0 - k0) * pp01;
            double new_p10 = -k1 * pp00 + pp10;
            double new_p11 = -k1 * pp01 + pp11;
            p00 = new_p00;
            p01 = new_p01;
            p10 = new_p10;
            p11 = new_p11;
            p01 = p10 = (p01 + p10) / 2.0;

            if (p00 < 1e-9) p00 = 1e-9;
            if (p11 < 1e-9) p11 = 1e-9;
        }

        double unc = Math.Sqrt(Math.Abs(p00));
        return new StructuralScore(
            evt.Timestamp,
            evt.Symbol,
            price,
            velocity,
            price + 2.0 * unc,
            price - 2.0 * unc,
            "sweep",
            0.0,
            0.0,
            0.0
        );
    }

    public static double ComputeDtw(double[] prototype, double[] candidate, int warpingWindow)
    {
        int n = prototype.Length;
        int m = candidate.Length;
        if (n == 0 || m == 0) return double.MaxValue;
        if (n > 50 || m > 50) return double.MaxValue;

        double[] prev = new double[m];
        double[] curr = new double[m];
        for (int j = 0; j < m; j++) prev[j] = double.MaxValue;

        for (int i = 0; i < n; i++)
        {
            int jStart = Math.Max(0, i - warpingWindow);
            int jEnd = Math.Min(m - 1, i + warpingWindow);

            for (int j = 0; j < m; j++) curr[j] = double.MaxValue;

            for (int j = jStart; j <= jEnd; j++)
            {
                double cost = Math.Abs(prototype[i] - candidate[j]);

                if (i == 0 && j == 0)
                {
                    curr[j] = cost;
                    continue;
                }

                double minPrev = double.MaxValue;
                if (i > 0 && prev[j] < minPrev) minPrev = prev[j];
                if (j > 0 && curr[j - 1] < minPrev) minPrev = curr[j - 1];
                if (i > 0 && j > 0 && prev[j - 1] < minPrev) minPrev = prev[j - 1];

                if (minPrev < double.MaxValue)
                    curr[j] = cost + minPrev;
            }

            if (i < n - 1)
            {
                bool anyFinite = false;
                for (int j = jStart; j <= jEnd; j++)
                    if (curr[j] < double.MaxValue) { anyFinite = true; break; }
                if (!anyFinite) return double.MaxValue;
            }

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[m - 1];
    }

    public static StructuralScore ScoreEvent(CepEvent evt, MarketEvent[] window)
    {
        var score = ScoreWithKalman(evt, window);

        if (window.Length >= 10)
        {
            double[] candidate = new double[10];
            int start = window.Length - 10;
            long eventTs = evt.Timestamp;

            int centerIdx = start;
            long bestDiff = long.MaxValue;
            for (int i = start; i < window.Length; i++)
            {
                long diff = Math.Abs(window[i].Timestamp - eventTs);
                if (diff < bestDiff) { bestDiff = diff; centerIdx = i; }
            }

            int w0 = Math.Max(0, centerIdx - 5);
            int w1 = Math.Min(window.Length - 1, w0 + 9);
            w0 = Math.Max(0, w1 - 9);
            for (int i = 0; i < 10; i++)
                candidate[i] = window[w0 + i].Price;

            double cMin = double.MaxValue, cMax = double.MinValue;
            for (int i = 0; i < 10; i++)
            {
                if (candidate[i] < cMin) cMin = candidate[i];
                if (candidate[i] > cMax) cMax = candidate[i];
            }

            double cRange = cMax - cMin;
            if (cRange == 0)
                return new StructuralScore(score.Timestamp, score.Symbol, score.StateMean, score.StateVelocity, score.UncertaintyUpper, score.UncertaintyLower, score.PatternFamily, 0.0, 0.0, score.AnomalyScore);
            for (int i = 0; i < 10; i++)
                candidate[i] = (candidate[i] - cMin) / cRange;

            double[] contProto = new double[10];
            double contMin = CONTINUATION_PROTOTYPE.Min(), contMax = CONTINUATION_PROTOTYPE.Max();
            double contRange = contMax - contMin;
            for (int i = 0; i < 10; i++)
                contProto[i] = contRange > 0 ? (CONTINUATION_PROTOTYPE[i] - contMin) / contRange : 0.0;
            double contDtw = ComputeDtw(contProto, candidate, WARPING_WINDOW);
            double continuationSim = 1.0 / (1.0 + contDtw);

            double[] revProto = new double[10];
            double revMin = REVERSAL_PROTOTYPE.Min(), revMax = REVERSAL_PROTOTYPE.Max();
            double revRange = revMax - revMin;
            for (int i = 0; i < 10; i++)
                revProto[i] = revRange > 0 ? (REVERSAL_PROTOTYPE[i] - revMin) / revRange : 0.0;
            double revDtw = ComputeDtw(revProto, candidate, WARPING_WINDOW);
            double reversalSim = 1.0 / (1.0 + revDtw);

            return new StructuralScore(
                score.Timestamp,
                score.Symbol,
                score.StateMean,
                score.StateVelocity,
                score.UncertaintyUpper,
                score.UncertaintyLower,
                score.PatternFamily,
                continuationSim,
                reversalSim,
                score.AnomalyScore
            );
        }

        return score;
    }

    public static BlackboardState WriteState(StructuralScore score, MarketEvent[] window)
    {
        int positiveDeltas = 0;
        int totalDeltas = 0;
        int evalCount = Math.Min(window.Length, 10);
        int start = window.Length - evalCount;
        for (int i = start + 1; i < window.Length; i++)
        {
            if (window[i].Price > window[i - 1].Price)
                positiveDeltas++;
            totalDeltas++;
        }

        string regime;
        if (positiveDeltas >= 7) regime = "uptrend";
        else if (positiveDeltas <= 3) regime = "downtrend";
        else regime = "chop";

        double regimeConfidence = totalDeltas > 0
            ? Math.Max(positiveDeltas, totalDeltas - positiveDeltas) / (double)totalDeltas
            : 0.0;

        return new BlackboardState(
            score.Timestamp,
            score.Symbol,
            score.PatternFamily == "sweep",
            score.PatternFamily,
            score.PatternSimilarity,
            score.ReversalSimilarity,
            score.StateVelocity,
            score.UncertaintyUpper,
            score.UncertaintyLower,
            score.AnomalyScore,
            regime,
            regimeConfidence,
            "hold"
        );
    }

    /// <summary>
    /// Produce a fresh BlackboardState from the current window for post-sweep re-evaluation.
    /// Uses a synthetic sweep event anchored at the last tick of the window so that
    /// ScoreEvent extracts the window correctly and SweepActive remains true.
    /// </summary>
    public static BlackboardState RefreshState(MarketEvent[] window, bool isBullishSweep)
    {
        var last = window[window.Length - 1];
        var synth = new CepEvent(last.Timestamp, last.Symbol, "SweepStart",
            last.Price, isBullishSweep ? "bullish" : "bearish");
        var score = ScoreEvent(synth, window);
        return WriteState(score, window);
    }

    // ═══════════════════════════════════════════════════════════════
    // ── NEW: Market-structure scoring path ────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Score using the new market-structure layer. Runs Kalman filter for velocity,
    /// calls StructureScorers.ScoreMarket() for convictions, then detects regime.
    /// Returns a fully populated MarketStructureScore.
    /// </summary>
    public static MarketStructureScore ScoreMarket(
        MarketEvent[] window, ActiveEventSnapshot events, ScoringConfig? config = null)
    {
        // ── Kalman filter for velocity context ────────────────────
        double vel = 0;
        if (window.Length > 0)
        {
            var synth = new CepEvent(window[^1].Timestamp, window[^1].Symbol,
                "SweepStart", window[^1].Price, events.IsBullishSweep ? "bullish" : "bearish");
            var kalman = ScoreWithKalman(synth, window);
            vel = kalman.StateVelocity;
        }

        // If snapshot doesn't have velocity, inject Kalman result
        var eventsWithVel = events;
        if (events.KalmanVelocity == 0 && vel != 0)
        {
            eventsWithVel = new ActiveEventSnapshot(events.SweepOrigin, events.IsBullishSweep,
                vel, events.DailyAvgVolume, events.RecentAvgVolume,
                events.IsVolumeExpanding, events.IsThinVolume, events.VolumeRatio,
                events.SwingHigh, events.SwingLow, events.CurrentSwingRange,
                events.LastSwingDirection, events.BullishBOS, events.BearishBOS,
                events.BOSPrice, events.BOSTimestamp,
                events.BullishCHoCH, events.BearishCHoCH, events.CHoCHTimestamp,
                events.Reclaim, events.Exhaustion, events.Absorption,
                events.LiquidationCluster, events.MomentumPersistence,
                events.CleanContinuation, events.Consolidation,
                events.DoubleStructure, events.StopHunt);
        }

        // ── Call structure scorers ────────────────────────────────
        var msScore = StructureScorers.ScoreMarket(window, eventsWithVel, config);

        // ── Record family scores for diagnostics ──────────────────
        double famCont = new[] { msScore.MomentumPersistScore, msScore.CleanContScore,
            msScore.PullbackResumeScore, msScore.ConsolidationScore,
            msScore.TrendContinuationScore }.Max();
        double famRev = new[] { msScore.SweepReclaimScore, msScore.ExhaustionScore,
            msScore.AbsorptionScore, msScore.LiqClusterScore,
            msScore.BreakoutFailScore, msScore.DoubleStructureScore }.Max();
        double famBOS = msScore.BOSScore;
        double famManip = Math.Max(msScore.StopHuntScore, msScore.LowLiquidityRejectScore);
        PrototypeDiagnostics.Instance.RecordFamilyScores(famCont, famRev, famBOS, famManip, msScore.MetaScore);

        // ── Regime detection (same logic as WriteState) ───────────
        var (regime, regimeConf) = DetectRegime(window);

        // ── Return with Kalman + regime populated ─────────────────
        return new MarketStructureScore(
            msScore.Timestamp, msScore.Symbol,
            vel, vel, 0, 0,
            regime, regimeConf,
            msScore.ContinuationConviction, msScore.ReversalConviction,
            msScore.ActiveStructures,
            msScore.SweepReclaimScore, msScore.BreakoutFailScore,
            msScore.ExhaustionScore, msScore.AbsorptionScore,
            msScore.LiqClusterScore, msScore.MomentumPersistScore,
            msScore.CleanContScore, msScore.PullbackResumeScore,
            msScore.LowLiquidityRejectScore,
            msScore.ConsolidationScore, msScore.DoubleStructureScore,
            msScore.StopHuntScore, msScore.TrendContinuationScore,
            msScore.BOSScore, msScore.CHoCHScore, msScore.MetaScore);
    }

    /// <summary>
    /// Convert a MarketStructureScore into BlackboardState.
    /// Uses the same regime detection as the old WriteState.
    /// </summary>
    public static BlackboardState WriteState(MarketStructureScore score, MarketEvent[] window)
    {
        var (regime, regimeConf) = DetectRegime(window);

        return new BlackboardState(
            score.Timestamp,
            score.Symbol,
            score.ActiveStructures != StructureFlags.None,  // SweepActive if any structure
            score.PatternFamily,
            score.PatternSimilarity,       // → ContinuationConviction
            score.ReversalSimilarity,      // → ReversalConviction
            score.StateVelocity,
            score.UncertaintyUpper,
            score.UncertaintyLower,
            score.AnomalyScore,
            string.IsNullOrEmpty(score.Regime) ? regime : score.Regime,
            score.RegimeConfidence > 0 ? score.RegimeConfidence : regimeConf,
            "hold"
        );
    }

    /// <summary>
    /// Produce a fresh BlackboardState using the new market-structure scoring path.
    /// Replaces the old RefreshState() that used DTW.
    /// </summary>
    public static BlackboardState RefreshState(
        MarketEvent[] window, ActiveEventSnapshot events, ScoringConfig? config = null)
    {
        var score = ScoreMarket(window, events, config);
        return WriteState(score, window);
    }

    /// <summary>
    /// Regime detection: uptrend if ≥7/10 ticks up, downtrend if ≤3/10, else chop.
    /// </summary>
    public static (string regime, double confidence) DetectRegime(MarketEvent[] window)
    {
        int positiveDeltas = 0, totalDeltas = 0;
        int evalCount = Math.Min(window.Length, 10);
        int start = window.Length - evalCount;
        for (int i = start + 1; i < window.Length; i++)
        {
            if (window[i].Price > window[i - 1].Price) positiveDeltas++;
            totalDeltas++;
        }
        string regime;
        if (positiveDeltas >= 7) regime = "uptrend";
        else if (positiveDeltas <= 3) regime = "downtrend";
        else regime = "chop";

        double confidence = totalDeltas > 0
            ? Math.Max(positiveDeltas, totalDeltas - positiveDeltas) / (double)totalDeltas
            : 0.0;
        return (regime, confidence);
    }
}
