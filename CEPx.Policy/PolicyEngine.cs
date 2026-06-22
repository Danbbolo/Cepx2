using CEPx.Core;

namespace CEPx.Policy;

public static class PolicyEngine
{
    private const double SIMILARITY_THRESHOLD = 0.35;
    private const double ANOMALY_THRESHOLD = 0.5;

    // ── Paper trading ─────────────────────────────────────────────────
    private const double COMMISSION_PCT = 0.05;
    private const double SLIPPAGE_PCT = 0.01;
    private const int MAX_HOLD_TICKS = 40;
    private const double STOP_LOSS_PCT = -1.0;
    private const double MOMENTUM_DECAY_SIM = 0.20;
    private const int DECLINE_TICKS = 3;
    private const double FLAT_VELOCITY = 0.1;
    private const double TRAPPED_REV_THRESHOLD = 0.3;
    private const double TRAPPED_ANOMALY_THRESHOLD = 0.3;
    private const int TRAPPED_MAX_TICKS = 5;

    public static bool InPosition;
    public static string PositionSide = "";
    public static double EntryPrice;
    public static double RawEntryPrice;
    public static int EntryTick;
    public static int TotalTrades;
    public static int WinningTrades;
    public static double TotalPnL;

    // ── Structural exit state ─────────────────────────────────────────
    private static readonly List<double> _patternSimHistory = new();
    private static double _sweepOriginPrice;
    private static bool _sweepIsBullish;
    private static int _ticksSinceEntry;

    public static void Reset()
    {
        InPosition = false;
        PositionSide = "";
        EntryPrice = 0;
        EntryTick = 0;
        _sweepOriginPrice = 0;
        _sweepIsBullish = false;
        _ticksSinceEntry = 0;
        _patternSimHistory.Clear();
    }

    public static void RecordPatternSimilarity(double sim)
    {
        _patternSimHistory.Add(sim);
        if (_patternSimHistory.Count > 50) _patternSimHistory.RemoveAt(0);
    }

    private static bool IsPatternSimilarityDeclining()
    {
        int n = _patternSimHistory.Count;
        if (n < DECLINE_TICKS) return false;
        for (int i = n - DECLINE_TICKS; i < n - 1; i++)
            if (_patternSimHistory[i] <= _patternSimHistory[i + 1])
                return false;
        return true;
    }

    // ── Policy ────────────────────────────────────────────────────────

    public static PolicyDecision Decide(BlackboardState state, int currentTickIndex = 0, double currentPrice = 0, double sweepOriginPrice = 0, bool isBullishSweep = false)
    {
        if (InPosition)
        {
            _ticksSinceEntry++;

            // 1. Momentum Decay (HIGHEST)
            bool simBelowThreshold = state.PatternSimilarity < MOMENTUM_DECAY_SIM;
            bool simDeclining = IsPatternSimilarityDeclining();
            bool flatAndDeclining = Math.Abs(state.KalmanVelocity) < FLAT_VELOCITY && simDeclining;
            if (simBelowThreshold || flatAndDeclining || simDeclining)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "momentum_decay", 1.0);

            // 2. Structural Invalidation
            if (currentPrice > 0 && sweepOriginPrice > 0)
            {
                if (PositionSide == "long" && isBullishSweep && currentPrice < sweepOriginPrice)
                    return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "structural_invalidation", 1.0);
                if (PositionSide == "short" && !isBullishSweep && currentPrice > sweepOriginPrice)
                    return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "structural_invalidation", 1.0);
            }

            // 3. Trapped Order Flow
            if (_ticksSinceEntry <= TRAPPED_MAX_TICKS
                && state.ReversalScore > TRAPPED_REV_THRESHOLD
                && state.AnomalyScore > TRAPPED_ANOMALY_THRESHOLD)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "trapped_order_flow", 1.0);

            // 4. Time Stop (safety net)
            if (currentTickIndex - EntryTick >= MAX_HOLD_TICKS)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "time_stop", 1.0);

            // 5. Stop Loss
            if (currentPrice > 0)
            {
                double unrealizedPnl = (currentPrice - EntryPrice) / EntryPrice * 100.0;
                if (PositionSide == "short") unrealizedPnl = -unrealizedPnl;
                if (unrealizedPnl <= STOP_LOSS_PCT)
                    return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "stop_loss", 1.0);
            }

            // 6. Reversal Signal
            if (state.ReversalScore >= 0.5)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "reversal_signal", 1.0);

            // 7. Velocity Flip
            if (PositionSide == "long" && state.KalmanVelocity < 0)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "velocity_flip", 1.0);
            if (PositionSide == "short" && state.KalmanVelocity > 0)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "velocity_flip", 1.0);
        }

        // ── ENTRY check ──
        if (!InPosition
            && state.SweepActive
            && state.PatternFamily == "sweep"
            && state.PatternSimilarity >= SIMILARITY_THRESHOLD
            && state.ReversalScore < 0.5
            && state.AnomalyScore < ANOMALY_THRESHOLD)
            return new PolicyDecision(state.Timestamp, state.Symbol, "enter", "long", "sweep_confirmed", 1.0);

        return new PolicyDecision(state.Timestamp, state.Symbol, "noop", "", "", 0.0);
    }

    // ── Paper execution ───────────────────────────────────────────────

    public static void PaperExecute(PolicyDecision decision, double currentPrice, string detector = "", double similarity = 0, double velocity = 0, int tickIndex = 0, double sweepOriginPrice = 0, bool isBullishSweep = false)
    {
        if (decision.Action == "enter" && !InPosition)
        {
            RawEntryPrice = currentPrice;
            EntryPrice = currentPrice * (1.0 + SLIPPAGE_PCT / 100.0);
            InPosition = true;
            PositionSide = decision.Side;
            EntryTick = tickIndex;
            _sweepOriginPrice = sweepOriginPrice;
            _sweepIsBullish = isBullishSweep;
            _ticksSinceEntry = 0;
            _patternSimHistory.Clear();
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
            Console.WriteLine($"PAPER EXIT @ {exitPrice:F2} PnL: {pnl:F2}% reason: {decision.Reason}");
            LogExit(exitPrice, decision.Reason, tickIndex);
            InPosition = false;
            PositionSide = "";
            _sweepOriginPrice = 0;
            _patternSimHistory.Clear();
            _ticksSinceEntry = 0;
        }
    }

    public static void PrintPaperSummary()
    {
        double winRate = TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
        Console.WriteLine($"Summary: {TotalTrades} trades, {winRate:F0}% win, Total PnL: {TotalPnL:F2}%");
        // Exit breakdown
        var breakdown = _exitReasons.GroupBy(r => r)
            .Select(g => $"{g.Count()} {g.Key}")
            .ToArray();
        if (breakdown.Length > 0)
            Console.WriteLine($"Exit breakdown: {string.Join(" | ", breakdown)}");
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
