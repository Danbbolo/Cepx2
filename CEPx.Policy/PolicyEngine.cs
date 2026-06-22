using CEPx.Core;

namespace CEPx.Policy;

public static class PolicyEngine
{
    private const double SIMILARITY_THRESHOLD = 0.65;
    private const double ANOMALY_THRESHOLD = 0.5;

    // ── Paper trading ─────────────────────────────────────────────────
    private const double COMMISSION_PCT = 0.05;
    private const double SLIPPAGE_PCT = 0.01;
    private const int MAX_HOLD_TICKS = 20;
    private const double STOP_LOSS_PCT = -0.5;

    public static bool InPosition;
    public static string PositionSide = "";
    public static double EntryPrice;
    public static double RawEntryPrice;
    public static int EntryTick;
    public static int TotalTrades;
    public static int WinningTrades;
    public static double TotalPnL;

    // ── Policy ────────────────────────────────────────────────────────

    public static PolicyDecision Decide(BlackboardState state)
    {
        if (state.SweepActive
            && state.PatternFamily == "sweep"
            && state.PatternSimilarity >= SIMILARITY_THRESHOLD
            && state.AnomalyScore < ANOMALY_THRESHOLD)
            return new PolicyDecision(state.Timestamp, state.Symbol, "enter", "long", "sweep_confirmed", 1.0);

        return new PolicyDecision(state.Timestamp, state.Symbol, "noop", "", "", 0.0);
    }

    // ── Paper execution ───────────────────────────────────────────────

    public static void PaperExecute(PolicyDecision decision, double currentPrice, string detector = "", double similarity = 0, double velocity = 0, int tickIndex = 0)
    {
        if (decision.Action == "enter" && !InPosition)
        {
            RawEntryPrice = currentPrice;
            EntryPrice = currentPrice * (1.0 + SLIPPAGE_PCT / 100.0);
            InPosition = true;
            PositionSide = decision.Side;
            EntryTick = tickIndex;
            Console.WriteLine($"PAPER ENTER {decision.Side} @ {EntryPrice:F2}");
            if (detector != "") LogEnter(EntryPrice, detector, similarity, velocity, tickIndex);
        }
        else if (decision.Action == "exit" && InPosition)
        {
            double exitPrice = currentPrice * (1.0 - SLIPPAGE_PCT / 100.0);
            double pnl = (exitPrice - EntryPrice) / EntryPrice * 100.0;
            if (PositionSide == "short") pnl = -pnl;
            pnl -= COMMISSION_PCT * 2;
            TotalTrades++;
            if (pnl > 0) WinningTrades++;
            TotalPnL += pnl;
            Console.WriteLine($"PAPER EXIT @ {exitPrice:F2} PnL: {pnl:F2}%");
            LogExit(exitPrice, decision.Reason, tickIndex);
            InPosition = false;
            PositionSide = "";
        }
    }

    public static void PrintPaperSummary()
    {
        double winRate = TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
        Console.WriteLine($"Summary: {TotalTrades} trades, {winRate:F0}% win, Total PnL: {TotalPnL:F2}%");
    }

    // ── Trade log for evidence ────────────────────────────────────────

    private static readonly List<long> _entryTimes = new(), _exitTimes = new();
    private static readonly List<double> _entryPrices = new(), _exitPrices = new(), _pnls = new(), _similarities = new(), _velocities = new();
    private static readonly List<string> _detectors = new(), _exitReasons = new();
    private static readonly List<int> _holdingTicks = new();
    private static double _pendingEntryPrice, _pendingSimilarity, _pendingVelocity;
    private static string _pendingDetector = "";
    private static int _pendingEntryTick;
    private static bool _hasPending;

    public static void LogEnter(double entryPrice, string detector, double similarity, double velocity, int tickIndex)
    {
        _pendingEntryPrice = entryPrice; _pendingDetector = detector;
        _pendingSimilarity = similarity; _pendingVelocity = velocity;
        _pendingEntryTick = tickIndex; _hasPending = true;
    }

    public static void LogExit(double exitPrice, string reason, int tickIndex)
    {
        if (!_hasPending) return;
        double pnl = (exitPrice - _pendingEntryPrice) / _pendingEntryPrice * 100;
        if (PositionSide == "short") pnl = -pnl;
        pnl -= COMMISSION_PCT * 2;
        _entryTimes.Add(_pendingEntryTick); _exitTimes.Add(tickIndex);
        _entryPrices.Add(_pendingEntryPrice); _exitPrices.Add(exitPrice);
        _pnls.Add(pnl); _detectors.Add(_pendingDetector); _exitReasons.Add(reason);
        _similarities.Add(_pendingSimilarity); _velocities.Add(_pendingVelocity);
        _holdingTicks.Add(tickIndex - _pendingEntryTick);
        _hasPending = false;
    }

    public static void PrintEvidenceReport()
    {
        int count = _entryTimes.Count;
        Console.WriteLine();
        Console.WriteLine("=== EVIDENCE REPORT ===");
        Console.WriteLine($"Total trades: {count}");
        if (count == 0) return;
        int wins = _pnls.Count(p => p > 0);
        double winRate = (double)wins / count * 100;
        Console.WriteLine($"Win rate: {winRate:F0}%");
        Console.WriteLine($"Avg PnL: {_pnls.Average():F2}%");

        var byDetector = _detectors.Select((d, i) => new { D = d, Win = _pnls[i] > 0 })
            .GroupBy(x => x.D)
            .Select(g => new { Detector = g.Key, Rate = (double)g.Count(x => x.Win) / g.Count() * 100 })
            .OrderByDescending(g => g.Rate).ToList();
        Console.WriteLine($"Best detector: {byDetector.FirstOrDefault()?.Detector ?? "none"} ({byDetector.FirstOrDefault()?.Rate ?? 0:F0}% win)");
        Console.WriteLine($"Worst detector: {byDetector.LastOrDefault()?.Detector ?? "none"} ({byDetector.LastOrDefault()?.Rate ?? 0:F0}% win)");
        Console.WriteLine($"Avg holding time: {_holdingTicks.Average():F0} ticks");

        var wVels = _pnls.Select((p, i) => new { Win = p > 0, Vel = _velocities[i] }).Where(x => x.Win).Select(x => x.Vel).ToArray();
        var lVels = _pnls.Select((p, i) => new { Win = p > 0, Vel = _velocities[i] }).Where(x => !x.Win).Select(x => x.Vel).ToArray();
        Console.WriteLine($"Kalman velocity avg (winners): {(wVels.Length > 0 ? wVels.Average() : 0):F2}");
        Console.WriteLine($"Kalman velocity avg (losers): {(lVels.Length > 0 ? lVels.Average() : 0):F2}");
    }

    public static void SaveTradeCsv(string path)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("EntryTime,ExitTime,EntryPrice,ExitPrice,PnL,Detector,Similarity,Velocity");
        for (int i = 0; i < _entryTimes.Count; i++)
            w.WriteLine($"{_entryTimes[i]},{_exitTimes[i]},{_entryPrices[i]:F2},{_exitPrices[i]:F2},{_pnls[i]:F2},{_detectors[i]},{_similarities[i]:F4},{_velocities[i]:F2}");
    }
}
