using CEPx.Core;
using CEPx.Blackboard;
using CEPx.Pipeline;
using CEPx.Policy;

const int WINDOW_SIZE = 50;
const int DURATION = 60;

// ── Test mode: replay ────────────────────────────────────────────
var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
RunReplayPipeline(ticks, writeToBlackboard: false);

// ── Phase 5: Live pipeline ───────────────────────────────────────

static void RunLivePipeline(string symbol, int durationSeconds)
{
    PipelineFunctions.LiveMode = true;
    BlackboardWriter.Connect();

    var window = new MarketEvent[WINDOW_SIZE];
    int count = 0;
    var lk = new object();

    var feed = PipelineFunctions.ConnectBinanceFeed(symbol, tick =>
    {
        lock (lk)
        {
            window[count % WINDOW_SIZE] = tick;
            count++;
            int n = Math.Min(count, WINDOW_SIZE);
            if (n < 5) return;

            var arr = new MarketEvent[n];
            if (count <= WINDOW_SIZE)
            {
                Array.Copy(window, 0, arr, 0, n);
            }
            else
            {
                int start = count % WINDOW_SIZE;
                int first = WINDOW_SIZE - start;
                Array.Copy(window, start, arr, 0, first);
                Array.Copy(window, 0, arr, first, start);
            }

            var sweep = PipelineFunctions.DetectSweepStart(arr);
            if (!sweep.HasValue) return;
            Console.WriteLine($"CEPx: SweepStart detected @ {sweep.Value.Price:F0}");
            var score = PipelineFunctions.ScoreEvent(sweep.Value, arr);
            Console.WriteLine($"Kalman: mean={score.StateMean:F2} vel={score.StateVelocity:F2}");
            var state = WriteState(score, arr);
            BlackboardWriter.Write(state);
            Console.WriteLine($"Blackboard: written {state.Symbol}");
            var decision = PolicyEngine.Decide(state);
            Console.WriteLine($"Policy: {decision.Action} {decision.Side} {decision.Reason}");
        }
    });

    Thread.Sleep(durationSeconds * 1000);
    feed.Dispose();
}

static void RunReplayPipeline(MarketEvent[] ticks, bool writeToBlackboard = true)
{
    if (writeToBlackboard)
        BlackboardWriter.Connect();

    var window = new List<MarketEvent>(WINDOW_SIZE);

    foreach (var tick in ticks)
    {
        window.Add(tick);
        while (window.Count > WINDOW_SIZE) window.RemoveAt(0);
        if (window.Count < 5) continue;

        var arr = window.ToArray();
        var sweep = PipelineFunctions.DetectSweepStart(arr);
        if (!sweep.HasValue) continue;
        Console.WriteLine($"CEPx: SweepStart detected @ {sweep.Value.Price:F0}");
        var score = PipelineFunctions.ScoreEvent(sweep.Value, arr);
        Console.WriteLine($"Kalman: mean={score.StateMean:F2} vel={score.StateVelocity:F2}");
        var state = WriteState(score, arr);

        if (writeToBlackboard)
        {
            BlackboardWriter.Write(state);
            Console.WriteLine($"Blackboard: written {state.Symbol}");
            var decision = PolicyEngine.Decide(state);
            Console.WriteLine($"Policy: {decision.Action} {decision.Side} {decision.Reason}");
        }
        else
        {
            Console.WriteLine($"Regime: {state.Regime} conf={state.RegimeConfidence:F2}");
        }
    }
}

static BlackboardState WriteState(StructuralScore score, MarketEvent[] window)
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
