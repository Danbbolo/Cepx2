using CEPx.Core;
using CEPx.Blackboard;
using CEPx.Pipeline;
using CEPx.Policy;

const int WINDOW_SIZE = 50;
const int DURATION = 60;

// ── Test mode: replay ────────────────────────────────────────────
var ticks = PipelineFunctions.SyntheticTicks("BTCUSDT");
RunReplayPipeline(ticks);

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
            var state = WriteState(score);
            BlackboardWriter.Write(state);
            Console.WriteLine($"Blackboard: written {state.Symbol}");
            var decision = PolicyEngine.Decide(state);
            Console.WriteLine($"Policy: {decision.Action} {decision.Side} {decision.Reason}");
        }
    });

    Thread.Sleep(durationSeconds * 1000);
    feed.Dispose();
}

static void RunReplayPipeline(MarketEvent[] ticks)
{
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
        var state = WriteState(score);
        BlackboardWriter.Write(state);
        Console.WriteLine($"Blackboard: written {state.Symbol}");
        var decision = PolicyEngine.Decide(state);
        Console.WriteLine($"Policy: {decision.Action} {decision.Side} {decision.Reason}");
    }
}

static BlackboardState WriteState(StructuralScore score)
{
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
        score.StateVelocity > 0 ? "uptrend" : "downtrend",
        Math.Abs(score.StateVelocity) / 100.0,
        "hold"
    );
}
