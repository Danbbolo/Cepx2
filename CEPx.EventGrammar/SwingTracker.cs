namespace CEPx.EventGrammar;

/// <summary>
/// Tracks swing highs and lows continuously from a price stream.
/// Foundation for BOS, CHoCH, breakout fail, pullback-resume, double top/bottom,
/// and stop-hunt detection.
///
/// Update every tick with the latest MarketEvent. The tracker maintains a rolling
/// buffer and detects swing points heuristically.
///
/// Lightweight: O(1) per update after initial buffer fill.
/// </summary>
public class SwingTracker
{
    // ── Configuration ──────────────────────────────────────────────
    private const int LOOKBACK_TICKS = 20;
    private const double SWING_MIN_RANGE_PCT = 0.05; // min % range for swing to count
    private const int MIN_TICKS_BETWEEN_SWINGS = 3;  // ticks between swing highs/lows
    private const int CHoCH_REVERSAL_TICKS = 5;      // max ticks for BOS→reversal = CHoCH

    // ── Rolling buffer ────────────────────────────────────────────
    private readonly double[] _prices = new double[LOOKBACK_TICKS];
    private readonly long[] _timestamps = new long[LOOKBACK_TICKS];
    private int _cursor;    // next write position
    private int _count;     // ticks seen (capped at LOOKBACK_TICKS)
    private int _totalTicks; // total ticks processed (unbounded)

    // ── Detected swing points ─────────────────────────────────────
    public double SwingHigh { get; private set; }
    public double SwingLow { get; private set; }
    public long SwingHighTimestamp { get; private set; }
    public long SwingLowTimestamp { get; private set; }

    /// <summary>Price range from last swing low to swing high (or vice versa).</summary>
    public double CurrentSwingRange => Math.Abs(SwingHigh - SwingLow);

    /// <summary>Direction of the most recent swing break: 1 = up, −1 = down, 0 = none.</summary>
    public int LastSwingDirection { get; private set; }

    // ── BOS (Break of Structure) state ────────────────────────────
    public bool BullishBOS { get; private set; }   // price broke above last swing high
    public bool BearishBOS { get; private set; }    // price broke below last swing low
    public long BOSTimestamp { get; private set; }
    public double BOSPrice { get; private set; }

    // ── CHoCH (Change of Character) state ─────────────────────────
    /// <summary>Bullish CHoCH: bearish BOS that reversed back up within CHoCH_REVERSAL_TICKS.</summary>
    public bool BullishCHoCH { get; private set; }
    /// <summary>Bearish CHoCH: bullish BOS that reversed back down within CHoCH_REVERSAL_TICKS.</summary>
    public bool BearishCHoCH { get; private set; }
    public long CHoCHTimestamp { get; private set; }

    // ── Price extremes in buffer (for swing detection) ────────────
    private double _bufferHigh, _bufferLow, _bufferHighPrev, _bufferLowPrev;
    private int _ticksSinceSwing;

    // ═══════════════════════════════════════════════════════════════
    // ── Public API ────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Update with latest price tick. Call every tick.</summary>
    public void Update(double price, long timestamp)
    {
        _totalTicks++;

        // ── Rolling buffer ────────────────────────────────────────
        _prices[_cursor] = price;
        _timestamps[_cursor] = timestamp;
        _cursor = (_cursor + 1) % LOOKBACK_TICKS;
        if (_count < LOOKBACK_TICKS) _count++;

        _ticksSinceSwing++;

        // ── Update buffer extremes ─────────────────────────────────
        if (_count >= LOOKBACK_TICKS)
        {
            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = 0; i < LOOKBACK_TICKS; i++)
            {
                if (_prices[i] > hi) hi = _prices[i];
                if (_prices[i] < lo) lo = _prices[i];
            }

            // Detect if extremes changed → new swing
            if (hi != _bufferHigh && hi > 0) DetectSwingPoint(hi, timestamp, isHigh: true);
            if (lo != _bufferLow && lo > 0) DetectSwingPoint(lo, timestamp, isHigh: false);

            _bufferHighPrev = _bufferHigh;
            _bufferLowPrev = _bufferLow;
            _bufferHigh = hi;
            _bufferLow = lo;
        }

        // ── BOS detection ─────────────────────────────────────────
        if (SwingHigh > 0 && price > SwingHigh && !BullishBOS)
            SetBOS(isBullish: true, price, timestamp);
        if (SwingLow > 0 && price < SwingLow && !BearishBOS)
            SetBOS(isBullish: false, price, timestamp);

        // ── CHoCH detection (BOS reversal) ────────────────────────
        if (BullishBOS && !BullishCHoCH && _totalTicks - BOSSampleTicks < CHoCH_REVERSAL_TICKS)
        {
            if (price < BOSPrice * 0.999) // reversed back below BOS level
            {
                BullishCHoCH = false; // it's actually bearish CHoCH
                BearishCHoCH = true;
                CHoCHTimestamp = timestamp;
            }
        }
        if (BearishBOS && !BearishCHoCH && _totalTicks - BOSSampleTicks < CHoCH_REVERSAL_TICKS)
        {
            if (price > BOSPrice * 1.001) // reversed back above BOS level
            {
                BearishCHoCH = false;
                BullishCHoCH = true;
                CHoCHTimestamp = timestamp;
            }
        }
    }
    private int BOSSampleTicks; // ticks at last BOS

    /// <summary>Reset all state (new day).</summary>
    public void Reset()
    {
        Array.Clear(_prices);
        Array.Clear(_timestamps);
        _cursor = 0; _count = 0; _totalTicks = 0;
        SwingHigh = 0; SwingLow = 0;
        SwingHighTimestamp = 0; SwingLowTimestamp = 0;
        LastSwingDirection = 0;
        BullishBOS = false; BearishBOS = false;
        BOSTimestamp = 0; BOSPrice = 0;
        BullishCHoCH = false; BearishCHoCH = false;
        CHoCHTimestamp = 0;
        _bufferHigh = 0; _bufferLow = 0;
        _bufferHighPrev = 0; _bufferLowPrev = 0;
        _ticksSinceSwing = 0;
        BOSSampleTicks = 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // ── Private ───────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    private void DetectSwingPoint(double price, long timestamp, bool isHigh)
    {
        // ── Minimum range check ───────────────────────────────────
        if (SwingLow > 0 && SwingHigh > 0)
        {
            double rangePct = (SwingHigh - SwingLow) / SwingLow * 100;
            if (rangePct < SWING_MIN_RANGE_PCT) return;
        }

        // ── Minimum time separation ───────────────────────────────
        if (_ticksSinceSwing < MIN_TICKS_BETWEEN_SWINGS) return;

        if (isHigh)
        {
            if (price > SwingHigh || SwingHigh == 0)
            {
                SwingHigh = price;
                SwingHighTimestamp = timestamp;
                LastSwingDirection = 1;
            }
        }
        else
        {
            if (price < SwingLow || SwingLow == 0)
            {
                SwingLow = price;
                SwingLowTimestamp = timestamp;
                LastSwingDirection = -1;
            }
        }
        _ticksSinceSwing = 0;

        // Clear stale BOS/CHoCH when new swing forms
        BullishBOS = false; BearishBOS = false;
        BullishCHoCH = false; BearishCHoCH = false;
    }

    private void SetBOS(bool isBullish, double price, long timestamp)
    {
        if (isBullish)
        {
            BullishBOS = true; BearishBOS = false;
        }
        else
        {
            BearishBOS = true; BullishBOS = false;
        }
        BOSTimestamp = timestamp;
        BOSPrice = price;
        BOSSampleTicks = _totalTicks;
        BullishCHoCH = false; BearishCHoCH = false;
    }

    // ── Diagnostic ─────────────────────────────────────────────────

    /// <summary>Print current swing state to console.</summary>
    public string DiagString()
    {
        string bos = BullishBOS ? "BOS↑" : BearishBOS ? "BOS↓" : "—";
        string choch = BullishCHoCH ? "CHoCH↑" : BearishCHoCH ? "CHoCH↓" : "";
        return $"Swing H={SwingHigh:F2} L={SwingLow:F2} range={CurrentSwingRange:F2}% dir={LastSwingDirection} {bos} {choch}";
    }
}
