using CEPx.Core;

namespace CEPx.Policy;

public static class PolicyEngine
{
    private const double SIMILARITY_THRESHOLD = 0.65;
    private const double ANOMALY_THRESHOLD = 0.5;

    // ── Paper trading ─────────────────────────────────────────────────
    private const double COMMISSION_PCT = 0.05;
    private const double SLIPPAGE_PCT = 0.01;
    private const double POSITION_SIZE = 0.01;

    public static bool InPosition;
    public static string PositionSide = "";
    public static double EntryPrice;
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

    public static void PaperExecute(PolicyDecision decision, double currentPrice)
    {
        if (decision.Action == "enter" && !InPosition)
        {
            EntryPrice = currentPrice * (1.0 + SLIPPAGE_PCT / 100.0);
            InPosition = true;
            PositionSide = decision.Side;
            Console.WriteLine($"PAPER ENTER {decision.Side} @ {EntryPrice:F2}");
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
            InPosition = false;
            PositionSide = "";
        }
    }

    public static void PrintPaperSummary()
    {
        double winRate = TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
        Console.WriteLine($"Summary: {TotalTrades} trades, {winRate:F0}% win, Total PnL: {TotalPnL:F2}%");
    }
}
