using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    private const double PROCESS_NOISE = 0.01;
    private const double MEASUREMENT_NOISE = 1.0;
    private const int WARPING_WINDOW = 3;

    private static readonly double[] SWEEP_PROTOTYPE =
        { 0.0, 0.05, 0.12, 0.21, 0.33, 0.48, 0.66, 0.87, 1.12, 1.40 };

    public static StructuralScore ScoreWithKalman(CepEvent evt, MarketEvent[] window)
    {
        if (window.Length == 0)
            return new StructuralScore(evt.Timestamp, evt.Symbol,
                0.0, 0.0, 0.0, 0.0, "sweep", 0.0, 0.0);

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
            0.0
        );
    }

    public static double ComputeDtw(double[] prototype, double[] candidate, int warpingWindow)
    {
        int n = prototype.Length;
        int m = candidate.Length;
        if (n == 0 || m == 0) return double.MaxValue;
        if (n > 50 || m > 50) return double.MaxValue; // DTW hard cap: max 50 ticks per sequence

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
            // DTW hard cap: candidate always 10 ticks, well within 50 limit
            double[] candidate = new double[10];
            int start = window.Length - 10;
            double cMin = double.MaxValue, cMax = double.MinValue;
            for (int i = 0; i < 10; i++)
            {
                candidate[i] = window[start + i].Price;
                if (candidate[i] < cMin) cMin = candidate[i];
                if (candidate[i] > cMax) cMax = candidate[i];
            }

            double cRange = cMax - cMin;
            if (cRange == 0)
                return new StructuralScore(score.Timestamp, score.Symbol, score.StateMean, score.StateVelocity, score.UncertaintyUpper, score.UncertaintyLower, score.PatternFamily, 0.0, score.AnomalyScore);
            if (cRange > 0)
                for (int i = 0; i < 10; i++)
                    candidate[i] = (candidate[i] - cMin) / cRange;

            double[] proto = new double[10];
            double pMin = SWEEP_PROTOTYPE[0], pMax = SWEEP_PROTOTYPE[9];
            double pRange = pMax - pMin;
            for (int i = 0; i < 10; i++)
                proto[i] = pRange > 0 ? (SWEEP_PROTOTYPE[i] - pMin) / pRange : 0.0;

            double dtw = ComputeDtw(proto, candidate, WARPING_WINDOW);
            double similarity = 1.0 / (1.0 + dtw);

            return new StructuralScore(
                score.Timestamp,
                score.Symbol,
                score.StateMean,
                score.StateVelocity,
                score.UncertaintyUpper,
                score.UncertaintyLower,
                score.PatternFamily,
                similarity,
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
        if (positiveDeltas >= 7)
            regime = "uptrend";
        else if (positiveDeltas <= 3)
            regime = "downtrend";
        else
            regime = "chop";

        double regimeConfidence = totalDeltas > 0
            ? Math.Max(positiveDeltas, totalDeltas - positiveDeltas) / (double)totalDeltas
            : 0.0;

        return new BlackboardState(
            score.Timestamp,
            score.Symbol,
            score.PatternFamily == "sweep",
            score.PatternFamily,
            score.PatternSimilarity,
            score.StateVelocity,
            score.UncertaintyUpper,
            score.UncertaintyLower,
            score.AnomalyScore,
            regime,
            regimeConfidence,
            "hold"
        );
    }
}
