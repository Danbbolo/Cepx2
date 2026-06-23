namespace CEPx.EventGrammar;

public class EventGrammarConfig
{
    // ── Sweep detection ──────────────────────────────────────────
    public double SweepPctThreshold { get; set; } = 0.2;
    public int SweepWindowTicks { get; set; } = 5;

    // ── Reclaim ──────────────────────────────────────────────────
    public int ReclaimWindow { get; set; } = 10;

    // ── Absorption ───────────────────────────────────────────────
    public double MinVolumeMultiplier { get; set; } = 2.0;
    public int AbsorptionWindow { get; set; } = 5;
    public double AbsorptionVolumeMultiplier { get; set; } = 3.0;

    /// <summary>Volume ratio normalization: this × average = score 1.0 for volume intensity.</summary>
    public double AbsorptionVolumeNorm { get; set; } = 8.0;

    /// <summary>Maximum price movement (% of price) for absorption detection (also used for stability scoring).</summary>
    public double AbsorptionMaxPriceMovePct { get; set; } = 0.1;

    // ── Breakout ─────────────────────────────────────────────────
    public double BreakoutPct { get; set; } = 0.2;
    public int BreakoutRangeWindow { get; set; } = 10;

    // ── Exhaustion ───────────────────────────────────────────────
    public double ExhaustionMovePct { get; set; } = 0.15;
    public double ExhaustionReversalRatio { get; set; } = 0.3;
    public int ExhaustionWindow { get; set; } = 6;
    public double ExhaustionStallRatio { get; set; } = 0.5;

    /// <summary>Stall ratio normalization: this ratio = score 1.0 for deceleration (lower = stronger).</summary>
    public double ExhaustionStallNorm { get; set; } = 0.15;

    /// <summary>Initial move normalization: this % move = score 1.0 for magnitude.</summary>
    public double ExhaustionMoveNormPct { get; set; } = 0.5;

    // ── Liquidation cluster detection ────────────────────────────
    /// <summary>Minimum number of liquidations against sweep direction to trigger.</summary>
    public int LiquidationMinCount { get; set; } = 3;

    /// <summary>Minimum total quantity (in BTC) of liquidations against sweep.</summary>
    public double LiquidationMinTotalQty { get; set; } = 10.0;

    /// <summary>Distance from sweep origin (as fraction of price) considered "near extreme".</summary>
    public double LiquidationProximityPct { get; set; } = 0.003;

    /// <summary>Count normalization target: this many liquidations = score of 1.0 for count dimension.</summary>
    public double LiquidationCountNorm { get; set; } = 8.0;

    /// <summary>Quantity normalization target: this much total qty = score of 1.0 for qty dimension.</summary>
    public double LiquidationQtyNorm { get; set; } = 50.0;

    /// <summary>Single large liquidation threshold: a single liq >= this qty gets a whale bonus.</summary>
    public double LiquidationWhaleQty { get; set; } = 20.0;

    /// <summary>Direction ratio target: this fraction of liqs against sweep = score of 1.0 for direction dimension.</summary>
    public double LiquidationDirRatioFullScore { get; set; } = 0.7;

    // ── Momentum persistence (continuation) ──────────────────────
    /// <summary>Number of ticks to analyze for momentum persistence.</summary>
    public int MomentumPersistenceWindowTicks { get; set; } = 10;

    /// <summary>Minimum net price move (% of price) required to consider momentum persistent.</summary>
    public double MomentumPersistenceMinNetMovePct { get; set; } = 0.15;

    /// <summary>Minimum fraction of ticks moving in the dominant direction.</summary>
    public double MomentumPersistenceDirectionConsistencyMin { get; set; } = 0.6;

    /// <summary>Normalization target for net move magnitude (this % move = score 1.0).</summary>
    public double MomentumPersistenceMoveNormPct { get; set; } = 0.5;

    // ── No meaningful absorption (continuation) ──────────────────
    /// <summary>Number of ticks to analyze for absence of absorption.</summary>
    public int NoAbsorptionWindowTicks { get; set; } = 5;

    /// <summary>Volume multiplier ceiling: any tick above this ratio vs average = potential absorption.</summary>
    public double NoAbsorptionMaxVolumeMultiplier { get; set; } = 2.5;

    /// <summary>Minimum net price move (% of price) required to trigger (tightened: was 0.1).</summary>
    public double NoAbsorptionMinNetMovePct { get; set; } = 0.2;

    /// <summary>Minimum score to fire (0.0-1.0). Filters out weak signals. Set to 0 to disable.</summary>
    public double NoAbsorptionMinScore { get; set; } = 0.5;

    /// <summary>Net move normalization: this % move = score 1.0 for continuation dimension.</summary>
    public double NoAbsorptionMoveNormPct { get; set; } = 0.4;
}
