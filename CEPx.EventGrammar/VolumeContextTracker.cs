namespace CEPx.EventGrammar;

/// <summary>
/// Tracks rolling volume context continuously from a market data stream.
/// Exposes daily average volume, recent average volume, and volume regime flags
/// (expanding, thin) for use by confirmation/invalidation signals and
/// low-liquidity detection.
///
/// Update every tick with the latest MarketEvent.
/// Lightweight: O(1) per update, constant memory.
/// </summary>
public class VolumeContextTracker
{
    // ── Configuration ──────────────────────────────────────────────
    private const int RECENT_WINDOW = 20;       // ticks for "recent" average
    private const double EXPANSION_RATIO = 1.5;  // vol > 1.5× recent = expanding
    private const double THIN_RATIO = 0.5;       // vol < 0.5× recent = thin

    // ── Rolling buffers ───────────────────────────────────────────
    private readonly double[] _recentVolumes = new double[RECENT_WINDOW];
    private int _recentCursor;
    private int _recentCount;

    // ── Daily stats ───────────────────────────────────────────────
    private double _dailyTotalVolume;
    private int _dailyTickCount;

    // ── Current tick ──────────────────────────────────────────────
    private double _lastVolume;

    // ═══════════════════════════════════════════════════════════════
    // ── Public properties ─────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Average volume across all ticks seen today (or since reset).</summary>
    public double DailyAvgVolume => _dailyTickCount > 0
        ? _dailyTotalVolume / _dailyTickCount : 0;

    /// <summary>Average volume over the last RECENT_WINDOW ticks.</summary>
    public double RecentAvgVolume
    {
        get
        {
            if (_recentCount == 0) return 0;
            double sum = 0;
            for (int i = 0; i < _recentCount; i++) sum += _recentVolumes[i];
            return sum / _recentCount;
        }
    }

    /// <summary>Volume of the most recent tick.</summary>
    public double LastVolume => _lastVolume;

    /// <summary>True if last tick's volume > EXPANSION_RATIO × recent average.</summary>
    public bool IsVolumeExpanding
    {
        get
        {
            double recent = RecentAvgVolume;
            return recent > 0 && _lastVolume > recent * EXPANSION_RATIO;
        }
    }

    /// <summary>True if last tick's volume < THIN_RATIO × recent average.</summary>
    public bool IsThinVolume
    {
        get
        {
            double recent = RecentAvgVolume;
            return recent > 0 && _lastVolume < recent * THIN_RATIO;
        }
    }

    /// <summary>Volume ratio: last / recent average. 1.0 = normal, >1.0 = elevated, <1.0 = thin.</summary>
    public double VolumeRatio
    {
        get
        {
            double recent = RecentAvgVolume;
            return recent > 0 ? _lastVolume / recent : 1.0;
        }
    }

    /// <summary>Number of ticks processed (for statistics).</summary>
    public int TotalTicks => _dailyTickCount;

    // ═══════════════════════════════════════════════════════════════
    // ── Public API ────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Update with latest tick. Call every tick.</summary>
    public void Update(double volume)
    {
        _lastVolume = volume;

        // ── Daily accumulator ─────────────────────────────────────
        _dailyTotalVolume += volume;
        _dailyTickCount++;

        // ── Recent rolling buffer ─────────────────────────────────
        _recentVolumes[_recentCursor] = volume;
        _recentCursor = (_recentCursor + 1) % RECENT_WINDOW;
        if (_recentCount < RECENT_WINDOW) _recentCount++;
    }

    /// <summary>Reset all state (new day).</summary>
    public void Reset()
    {
        Array.Clear(_recentVolumes);
        _recentCursor = 0;
        _recentCount = 0;
        _dailyTotalVolume = 0;
        _dailyTickCount = 0;
        _lastVolume = 0;
    }

    // ── Diagnostic ─────────────────────────────────────────────────

    /// <summary>Print current volume context to console.</summary>
    public string DiagString()
    {
        string flag = IsVolumeExpanding ? "EXPAND" : IsThinVolume ? "THIN" : "NORM";
        return $"Vol last={_lastVolume:F1} recent={RecentAvgVolume:F1} daily={DailyAvgVolume:F1} ratio={VolumeRatio:F2} {flag}";
    }
}
