using CEPx.Core;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CEPx.Pipeline;

public static class PipelineFunctions
{
    private const double SWEEP_THRESHOLD_PCT = 0.5;
    private const int SWEEP_WINDOW_TICKS = 5;
    private const double MIN_VOLUME_MULTIPLIER = 2.0;

    private const double PROCESS_NOISE = 0.01;
    private const double MEASUREMENT_NOISE = 1.0;
    private const int WARPING_WINDOW = 3;

    private static readonly double[] SWEEP_PROTOTYPE =
        { 0.0, 0.05, 0.12, 0.21, 0.33, 0.48, 0.66, 0.87, 1.12, 1.40 };

    public static bool LiveMode;

    // ── Phase 0 stubs ──────────────────────────────────────────────────

    public static CepEvent? IngestTick(MarketEvent tick)
    {
        return new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "");
    }

    public static BlackboardState WriteState(StructuralScore score)
    {
        return new BlackboardState(0L, "", false, "", 0.0, 0.0, 0.0, 0.0, 0.0, "", 0.0, "");
    }

    public static PolicyDecision Decide(BlackboardState state)
    {
        return Policy.PolicyEngine.Decide(state);
    }

    public static CepEvent[] ReplayTicks(MarketEvent[] ticks)
    {
        return new[] { new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "") };
    }

    // ── Phase 2: Kalman + DTW scoring ──────────────────────────────────

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
            p00 = (1.0 - k0) * pp00;
            p01 = (1.0 - k0) * pp01;
            p10 = pp10 - k1 * pp00;
            p11 = pp11 - k1 * pp01;
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
            double cMin = double.MaxValue, cMax = double.MinValue;
            for (int i = 0; i < 10; i++)
            {
                candidate[i] = window[start + i].Price;
                if (candidate[i] < cMin) cMin = candidate[i];
                if (candidate[i] > cMax) cMax = candidate[i];
            }

            double cRange = cMax - cMin;
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

    // ── Phase 1: Ingestion + SweepStart ────────────────────────────────

    /// Stream live ticks to pipeline.
    public static IDisposable ConnectBinanceFeed(string symbol, Action<MarketEvent> onTick)
    {
        if (!LiveMode)
        {
            var cts = new CancellationTokenSource();
            var sym = symbol;
            _ = Task.Run(async () =>
            {
                foreach (var tick in SyntheticTicks(sym))
                {
                    if (cts.IsCancellationRequested) break;
                    onTick(tick);
                    await Task.Delay(100);
                }
            });
            return cts;
        }

        var cancel = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    var uri = $"wss://fstream.binance.com/ws/{symbol.ToLower()}@aggTrade";
                    await ws.ConnectAsync(new Uri(uri), cancel.Token);
                    var buffer = new byte[4096];
                    while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buffer, cancel.Token);
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var tick = ParseAggTrade(json, symbol);
                        if (tick.HasValue) onTick(tick.Value);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WS: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        });
        return cancel;
    }

    /// Find price-volume sweep in window.
    public static CepEvent? DetectSweepStart(MarketEvent[] window)
    {
        if (window.Length < SWEEP_WINDOW_TICKS) return null;
        var recent = new ArraySegment<MarketEvent>(window, window.Length - SWEEP_WINDOW_TICKS, SWEEP_WINDOW_TICKS);
        var prices = new double[SWEEP_WINDOW_TICKS];
        var volumes = new double[SWEEP_WINDOW_TICKS];
        for (int i = 0; i < SWEEP_WINDOW_TICKS; i++) { prices[i] = recent[i].Price; volumes[i] = recent[i].Volume; }
        var high = prices.Max();
        var low = prices.Min();
        var avgPrice = prices.Average();
        if (avgPrice <= 0) return null;
        if ((high - low) / avgPrice * 100 < SWEEP_THRESHOLD_PCT) return null;
        var avgVol = volumes.Average();
        if (avgVol <= 0) return null;
        var cur = recent[^1];
        if (cur.Volume <= avgVol * MIN_VOLUME_MULTIPLIER) return null;
        return new CepEvent(cur.Timestamp, cur.Symbol, "SweepStart", cur.Price, "");
    }

    /// Full replay for testing validation.
    public static CepEvent[] RunPipeline(MarketEvent[] ticks)
    {
        var events = new List<CepEvent>();
        var buf = new MarketEvent[SWEEP_WINDOW_TICKS];
        for (int i = 0; i < ticks.Length; i++)
        {
            buf[i % SWEEP_WINDOW_TICKS] = ticks[i];
            if (i < SWEEP_WINDOW_TICKS - 1) continue;
            var window = new MarketEvent[SWEEP_WINDOW_TICKS];
            for (int j = 0; j < SWEEP_WINDOW_TICKS; j++) window[j] = buf[(i - SWEEP_WINDOW_TICKS + 1 + j) % SWEEP_WINDOW_TICKS];
            var hit = DetectSweepStart(window);
            if (hit.HasValue) events.Add(hit.Value);
        }
        return events.ToArray();
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static MarketEvent? ParseAggTrade(string json, string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var price = double.Parse(r.GetProperty("p").GetString()!);
            var qty = double.Parse(r.GetProperty("q").GetString()!);
            var ts = r.GetProperty("T").GetInt64();
            return new MarketEvent(ts, symbol, price, qty, 0, 0, 0);
        }
        catch { return null; }
    }

    private static MarketEvent[] SyntheticTicks(string symbol)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new[]
        {
            new MarketEvent(now,       symbol, 42000.0, 1.0, 0, 0, 0),
            new MarketEvent(now+100,   symbol, 42030.0, 1.2, 0, 0, 0),
            new MarketEvent(now+200,   symbol, 42080.0, 1.5, 0, 0, 0),
            new MarketEvent(now+300,   symbol, 42150.0, 2.1, 0, 0, 0),
            new MarketEvent(now+400,   symbol, 42300.0, 5.0, 0, 0, 0),
        };
    }
}