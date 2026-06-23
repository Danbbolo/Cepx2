using CEPx.Core;

namespace CEPx.Policy;

public static class PolicyEngine
{
    private const double SIMILARITY_THRESHOLD = 0.35;
    private const double ANOMALY_THRESHOLD = 0.5;
    private const double ENTRY_VELOCITY_THRESHOLD = 0.5;

    // ── Paper trading ─────────────────────────────────────────────────
    private const double COMMISSION_PCT = 0.05;
    private const double SLIPPAGE_PCT = 0.01;
    private const long MAX_HOLD_TIME_MS = 3_600_000; // 1 hour
    private const int MAX_HOLD_TICKS = 40; // tick fallback for tests
    private const double STOP_LOSS_PCT = -1.0;
    private const double MOMENTUM_DECAY_SIM = 0.20;
    private const double MOMENTUM_DECAY_SIM_ABSOLUTE = 0.15;
    private const int DECLINE_TICKS = 3;
    private const double FLAT_VELOCITY = 0.1;
    private const double TRAPPED_REV_THRESHOLD = 0.3;
    private const double TRAPPED_ANOMALY_THRESHOLD = 0.3;
    private const long TRAPPED_MAX_TIME_MS = 300_000; // 5 minutes
    private const int TRAPPED_MAX_TICKS = 5; // tick fallback for tests
    private const int MOMENTUM_DECAY_CONFIRM = 4; // tick fallback for tests
    private const int VELOCITY_FLIP_CONFIRM = 8; // tick fallback for tests
    private const long VELOCITY_FLIP_TIME_MS = 600_000; // 10 minutes
    private const long MOMENTUM_DECAY_TIME_MS = 300_000; // 5 minutes
    private const double VELOCITY_FLIP_MIN_MAGNITUDE = 0.8;
    private const double VELOCITY_FLIP_SIM_GATE = 0.25;
    private const double MODE_B_REVERSAL_THRESHOLD = 0.32;
    private const double MODE_B_REV_MARGIN = 0.0; // disabled — margin hurts PnL
    private const double MODE_B_ANOMALY_MAX = 0.4;
    private const int VELOCITY_HISTORY_TICKS = 5;
    private const long MODE_B_MAX_SWEEP_AGE_MS = 480_000; // 8 minutes
    private const long EVENT_SIGNAL_TTL_MS = 120_000; // 2 minutes — how long event signals stay valid
    private const long LIQ_CLUSTER_TTL_MS = 300_000; // 5 minutes — liquidation clusters live longer

    // ── Mode B reversal boost tiers (strongest → weakest, parallel to Mode A) ──
    // Tier 1: liq cluster + any companion (combo)
    private const double MODE_B_LIQ_COMBO_BASE = 0.18;
    private const double MODE_B_LIQ_COMBO_EXTRA = 0.05; // if liq score >= 0.85
    // Tier 2: exhaustion + liq companion (combo)
    private const double MODE_B_EXH_COMBO_SCALE = 0.25;
    private const double MODE_B_EXH_COMBO_MIN = 0.10;
    private const double MODE_B_EXH_COMBO_MAX = 0.22;
    // Tier 3: liq standalone
    private const double MODE_B_LIQ_SOLO_SCALE = 0.25;
    private const double MODE_B_LIQ_SOLO_MIN = 0.08;
    private const double MODE_B_LIQ_SOLO_MAX = 0.20;
    // Tier 4: exhaustion standalone
    private const double MODE_B_EXH_SOLO_SCALE = 0.22;
    private const double MODE_B_EXH_SOLO_MIN = 0.06;
    private const double MODE_B_EXH_SOLO_MAX = 0.18;
    // Tier 5: absorption/reclaim + liq companion (combo)
    private const double MODE_B_EVT_COMBO_SCALE = 0.18;
    private const double MODE_B_EVT_COMBO_MIN = 0.06;
    private const double MODE_B_EVT_COMBO_MAX = 0.16;
    private const double MODE_B_EVT_COMBO_FLAT = 0.03;
    // Tier 6: absorption/reclaim standalone
    private const double MODE_B_EVT_SOLO_SCALE = 0.14;
    private const double MODE_B_EVT_SOLO_MIN = 0.03;
    private const double MODE_B_EVT_SOLO_MAX = 0.12;
    private const long CONT_SIGNAL_TTL_MS = 120_000; // 2 minutes — how long continuation signals stay valid

    // ── Mode A continuation boost tiers (strongest → weakest) ────
    // Tier 1: dual cont (MomentumPersistence + NoMeaningfulAbsorption both active)
    private const double MODE_A_DUAL_BOOST_SCALE = 0.40;
    private const double MODE_A_DUAL_BOOST_MIN = 0.10;
    private const double MODE_A_DUAL_BOOST_MAX = 0.25;
    // Tier 2: cont + trend-aligned regime
    private const double MODE_A_TREND_BOOST_SCALE = 0.30;
    private const double MODE_A_TREND_BOOST_MIN = 0.08;
    private const double MODE_A_TREND_BOOST_MAX = 0.22;
    private const double MODE_A_TREND_COMBO_FLAT = 0.05; // flat bonus for trend alignment
    // Tier 3: cont + strong Kalman velocity
    private const double MODE_A_VEL_BOOST_SCALE = 0.30;
    private const double MODE_A_VEL_BOOST_MIN = 0.05;
    private const double MODE_A_VEL_BOOST_MAX = 0.20;
    // Tier 4: cont standalone (no favorable regime or velocity)
    private const double MODE_A_SOLO_BOOST_SCALE = 0.25;
    private const double MODE_A_SOLO_BOOST_MIN = 0.03;
    private const double MODE_A_SOLO_BOOST_MAX = 0.18;

    public static bool InPosition;
    public static string PositionSide = "";
    public static double EntryPrice;
    public static double RawEntryPrice;
    public static int EntryTick;
    public static int TotalTrades;
    public static int WinningTrades;
    public static double TotalPnL;
    public static int ModeACount;
    public static int ModeBCount;
    public static int MomDecayExits;
    public static int VelFlipExits;
    public static int RevSigExits;
    public static int OtherExits;

    // ── Signal activity counters (for diagnostics) ──────────────
    public static int AbsorptionCount;
    public static int ExhaustionCount;
    public static int ReclaimCount;
    public static int LiquidationClusterCount;
    public static int MomentumPersistenceCount;
    public static int NoAbsorptionCount;
    public static int ModeAWithContSignal; // Mode A entries where a cont signal was active
    public static int ModeBWithRevSignal;  // Mode B entries where a reversal signal gave a boost

    // ── Structural exit state ─────────────────────────────────────────
    private static readonly List<double> _patternSimHistory = new();
    private static double _sweepOriginPrice;
    private static bool _sweepIsBullish;
    private static long _entryStartMs; // timestamp when position was entered
    private static int _ticksSinceEntry; // tick fallback for tests
    private static int _momentumDecayTicks; // tick fallback
    private static long _momentumDecayStartMs;
    private static int _velocityFlipTicks; // tick fallback
    private static long _velocityFlipStartMs;
    private static long _lastSweepMs; // timestamp of last sweep
    private static string _lastEventType = ""; // most recent EventGrammar signal (absorption/reclaim)
    private static long _lastEventTimestamp; // when it fired
    private static double _lastEventScore; // 0.0–1.0 severity score for absorption/reclaim
    private static bool _eventHadLiqCompanion; // true if liq cluster was active when absorption/reclaim fired
    private static string _lastExhaustionType = ""; // "ExhaustionPulse" or "ExhaustionAfterSweep" or ""
    private static long _lastExhaustionTimestamp;
    private static double _lastExhaustionScore;
    private static bool _exhaustionHadLiqCompanion; // true if liq cluster was active when exhaustion fired
    private static string _lastLiqType = ""; // most recent liquidation cluster direction
    private static long _lastLiqTimestamp; // when the liquidation cluster fired
    private static double _liqClusterScore; // 0.0–1.0 severity score
    private static bool _liqHadCompanion; // true if absorption/exhaustion was also active when liq fired

    // ── Continuation signal state (tracked separately per type) ──
    private static string _lastMomPerType = ""; // "MomentumPersistence" or ""
    private static long _lastMomPerTimestamp;
    private static double _lastMomPerScore;
    private static string _lastNoAbsType = ""; // "NoMeaningfulAbsorption" or ""
    private static long _lastNoAbsTimestamp;
    private static double _lastNoAbsScore;

    private static readonly List<double> _recentVelocities = new();

    public static void Reset()
    {
        InPosition = false;
        PositionSide = "";
        EntryPrice = 0;
        EntryTick = 0;
        _sweepOriginPrice = 0;
        _sweepIsBullish = false;
        _entryStartMs = 0;
        _ticksSinceEntry = 0;
        _momentumDecayTicks = 0; _momentumDecayStartMs = 0;
        _velocityFlipTicks = 0; _velocityFlipStartMs = 0;
        _recentVelocities.Clear();
        _patternSimHistory.Clear();
        _entryReasons.Clear();
        _lastLiqType = "";
        _lastLiqTimestamp = 0;
        _liqClusterScore = 0;
        _liqHadCompanion = false;
        _eventHadLiqCompanion = false;
        _lastExhaustionType = "";
        _lastExhaustionTimestamp = 0;
        _lastExhaustionScore = 0;
        _exhaustionHadLiqCompanion = false;
        _lastMomPerType = "";
        _lastMomPerTimestamp = 0;
        _lastMomPerScore = 0;
        _lastNoAbsType = "";
        _lastNoAbsTimestamp = 0;
        _lastNoAbsScore = 0;
        ModeACount = 0;
        ModeBCount = 0;
        MomDecayExits = 0;
        VelFlipExits = 0;
        RevSigExits = 0;
        OtherExits = 0;
        AbsorptionCount = 0;
        ExhaustionCount = 0;
        ReclaimCount = 0;
        LiquidationClusterCount = 0;
        MomentumPersistenceCount = 0;
        NoAbsorptionCount = 0;
        ModeAWithContSignal = 0;
        ModeBWithRevSignal = 0;
    }

    public static void RecordPatternSimilarity(double sim)
    {
        _patternSimHistory.Add(sim);
        if (_patternSimHistory.Count > 50) _patternSimHistory.RemoveAt(0);
    }

    public static void RecordVelocity(double vel)
    {
        _recentVelocities.Add(vel);
        if (_recentVelocities.Count > VELOCITY_HISTORY_TICKS) _recentVelocities.RemoveAt(0);
    }

    /// <summary>Feed EventGrammar signals into the BT for decision support.</summary>
    public static void RecordEvent(CepEvent evt)
    {
        if (evt.Type == "AbsorptionAfterSweep")
        {
            AbsorptionCount++;
            _lastEventType = evt.Type;
            _lastEventTimestamp = evt.Timestamp;
            _lastEventScore = ParseEventScore(evt.Context);
            _eventHadLiqCompanion = _lastLiqType != "" &&
                evt.Timestamp - _lastLiqTimestamp <= LIQ_CLUSTER_TTL_MS;
        }
        else if (evt.Type == "ExhaustionAfterSweep" || evt.Type == "ExhaustionPulse")
        {
            ExhaustionCount++;
            _lastExhaustionType = evt.Type;
            _lastExhaustionTimestamp = evt.Timestamp;
            _lastExhaustionScore = ParseEventScore(evt.Context);
            _exhaustionHadLiqCompanion = _lastLiqType != "" &&
                evt.Timestamp - _lastLiqTimestamp <= LIQ_CLUSTER_TTL_MS;
        }
        else if (evt.Type == "Reclaim")
        {
            ReclaimCount++;
            _lastEventType = evt.Type;
            _lastEventTimestamp = evt.Timestamp;
            _lastEventScore = ParseEventScore(evt.Context);
            _eventHadLiqCompanion = _lastLiqType != "" &&
                evt.Timestamp - _lastLiqTimestamp <= LIQ_CLUSTER_TTL_MS;
        }
        else if (evt.Type == "LiquidationCluster")
        {
            LiquidationClusterCount++;
            _lastLiqType = evt.Type;
            _lastLiqTimestamp = evt.Timestamp;
            _liqClusterScore = ParseLiqScore(evt.Context);

            // Check if any companion (absorption, reclaim, or exhaustion) was active
            bool genericActive = _lastEventType != "" &&
                evt.Timestamp - _lastEventTimestamp <= EVENT_SIGNAL_TTL_MS;
            bool exhaustionActive = _lastExhaustionType != "" &&
                evt.Timestamp - _lastExhaustionTimestamp <= EVENT_SIGNAL_TTL_MS;
            _liqHadCompanion = genericActive || exhaustionActive;
        }
        else if (evt.Type == "MomentumPersistence")
        {
            MomentumPersistenceCount++;
            _lastMomPerType = evt.Type;
            _lastMomPerTimestamp = evt.Timestamp;
            _lastMomPerScore = ParseEventScore(evt.Context);
        }
        else if (evt.Type == "NoMeaningfulAbsorption")
        {
            NoAbsorptionCount++;
            _lastNoAbsType = evt.Type;
            _lastNoAbsTimestamp = evt.Timestamp;
            _lastNoAbsScore = ParseEventScore(evt.Context);
        }
    }

    /// <summary>Parse event score from context string (format: "score:0.72" or "prefix:score:0.65").</summary>
    private static double ParseEventScore(string context)
    {
        if (string.IsNullOrEmpty(context)) return 0.5;
        // Find "score:" substring and parse the number after it
        int idx = context.LastIndexOf("score:");
        if (idx < 0) return 0.5;
        string numPart = context.Substring(idx + 6).Split(':')[0];
        if (double.TryParse(numPart,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score))
            return score;
        return 0.5;
    }

    /// <summary>Parse liquidation cluster score from context string.</summary>
    private static double ParseLiqScore(string context)
    {
        if (string.IsNullOrEmpty(context)) return 0.5;
        var parts = context.Split(':');
        if (parts.Length >= 2 && double.TryParse(parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score))
            return score;
        return 0.5;
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private static bool HasVelocityDirectionChanged()
    {
        int n = _recentVelocities.Count;
        if (n < 3) return false;
        // Check if most recent velocities went from positive to negative or vice versa
        int recent = _recentVelocities[n - 1] > 0 ? 1 : -1;
        int older = _recentVelocities[n - 3] > 0 ? 1 : -1;
        return recent != older;
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
        // Record velocity every tick for direction-change detection
        RecordVelocity(state.KalmanVelocity);
        if (state.SweepActive) _lastSweepMs = state.Timestamp;

        if (InPosition)
        {
            _ticksSinceEntry++;

            // 1. Momentum Decay (HIGHEST) — (sim<0.20 AND declining) OR (sim<0.15), 3-tick confirm
            bool simBelowThreshold = state.PatternSimilarity < MOMENTUM_DECAY_SIM;
            bool simBelowAbsolute = state.PatternSimilarity < MOMENTUM_DECAY_SIM_ABSOLUTE;
            bool simDeclining = IsPatternSimilarityDeclining();
            if ((simBelowThreshold && simDeclining) || simBelowAbsolute)
            {
                if (_momentumDecayStartMs == 0) _momentumDecayStartMs = state.Timestamp;
                _momentumDecayTicks++;
            }
            else
            {
                _momentumDecayStartMs = 0;
                _momentumDecayTicks = 0;
            }
            bool momDecayFired = state.Timestamp > 0
                ? (_momentumDecayStartMs > 0 && state.Timestamp - _momentumDecayStartMs >= MOMENTUM_DECAY_TIME_MS)
                : (_momentumDecayTicks >= MOMENTUM_DECAY_CONFIRM);
            if (momDecayFired)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "momentum_decay", 1.0);

            // 2. Structural Invalidation
            if (currentPrice > 0 && sweepOriginPrice > 0)
            {
                if (PositionSide == "long" && isBullishSweep && currentPrice < sweepOriginPrice)
                    return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "structural_invalidation", 1.0);
                if (PositionSide == "short" && !isBullishSweep && currentPrice > sweepOriginPrice)
                    return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "structural_invalidation", 1.0);
            }

            // 3. Trapped Order Flow — within first 5 minutes of entry
            bool trappedFired = _entryStartMs > 0
                ? (state.Timestamp - _entryStartMs <= TRAPPED_MAX_TIME_MS)
                : (_ticksSinceEntry <= TRAPPED_MAX_TICKS);
            if (trappedFired
                && state.ReversalScore > TRAPPED_REV_THRESHOLD
                && state.AnomalyScore > TRAPPED_ANOMALY_THRESHOLD)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "trapped_order_flow", 1.0);

            // 4. Time Stop (safety net) — 1 hour max hold (40-tick fallback for tests)
            bool timeStopFired = _entryStartMs > 0
                ? (state.Timestamp - _entryStartMs >= MAX_HOLD_TIME_MS)
                : (_ticksSinceEntry >= 40);
            if (timeStopFired)
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

            // 7. Velocity Flip — only fire if pattern thesis is ALSO dying (sim < 0.30)
            bool velWrong = (PositionSide == "long" && state.KalmanVelocity < -VELOCITY_FLIP_MIN_MAGNITUDE)
                         || (PositionSide == "short" && state.KalmanVelocity > VELOCITY_FLIP_MIN_MAGNITUDE);
            bool patternDying = state.PatternSimilarity < VELOCITY_FLIP_SIM_GATE;
            if (velWrong && patternDying)
            {
                if (_velocityFlipStartMs == 0) _velocityFlipStartMs = state.Timestamp;
                _velocityFlipTicks++;
            }
            else
            {
                _velocityFlipStartMs = 0;
                _velocityFlipTicks = 0;
            }
            bool velFlipFired = state.Timestamp > 0
                ? (_velocityFlipStartMs > 0 && state.Timestamp - _velocityFlipStartMs >= VELOCITY_FLIP_TIME_MS)
                : (_velocityFlipTicks >= VELOCITY_FLIP_CONFIRM);
            if (velFlipFired)
                return new PolicyDecision(state.Timestamp, state.Symbol, "exit", "", "velocity_flip", 1.0);
        }

        // ── ENTRY: Two-mode Behavior Tree ──
        if (!InPosition && state.SweepActive && state.AnomalyScore < ANOMALY_THRESHOLD)
        {
            bool isBull = isBullishSweep;
            double vel = state.KalmanVelocity;
            double rev = state.ReversalScore;
            string regime = state.Regime;

            // MODE A: Continuation (trend-aligned momentum)
            // Multi-tier gradient boosting system — parallel to Mode B's 4-tier reversal logic.
            // Each tier uses score-dependent velocity threshold relaxation.
            //
            // Tier 1: DUAL cont — MomentumPersistence + NoMeaningfulAbsorption both active
            // Tier 2: Cont + trend — either cont signal + trend-aligned regime
            // Tier 3: Cont + velocity — either cont signal + strong Kalman velocity
            // Tier 4: Cont standalone — one cont signal, no favorable regime/velocity
            // Tier 5: Classic — no cont signals, trend-aligned + velStrong (unchanged)

            bool trendAligned = (isBull && regime == "uptrend") || (!isBull && regime == "downtrend");

            // ── Check active continuation signals ────────────────
            bool momPerActive = _lastMomPerType != "" &&
                state.Timestamp - _lastMomPerTimestamp <= CONT_SIGNAL_TTL_MS;
            bool noAbsActive = _lastNoAbsType != "" &&
                state.Timestamp - _lastNoAbsTimestamp <= CONT_SIGNAL_TTL_MS;
            bool dualContActive = momPerActive && noAbsActive;
            bool anyContActive = momPerActive || noAbsActive;

            // Best continuation score (max of whichever is active)
            double contScore = 0;
            if (momPerActive && noAbsActive)
                contScore = (_lastMomPerScore + _lastNoAbsScore) / 2.0; // dual: average
            else if (momPerActive)
                contScore = _lastMomPerScore;
            else if (noAbsActive)
                contScore = _lastNoAbsScore;

            // Kalman velocity companion: is velocity strong in sweep direction?
            bool velStrong = (isBull && vel > ENTRY_VELOCITY_THRESHOLD)
                          || (!isBull && vel < -ENTRY_VELOCITY_THRESHOLD);

            // ── Compute effective velocity threshold by tier ─────
            double effectiveVelThreshold = ENTRY_VELOCITY_THRESHOLD;
            bool entryAllowed = false;

            if (dualContActive)
            {
                // Tier 1: both signals active — strongest boost
                double boost = Clamp(contScore * MODE_A_DUAL_BOOST_SCALE,
                    MODE_A_DUAL_BOOST_MIN, MODE_A_DUAL_BOOST_MAX);
                effectiveVelThreshold -= boost;
                // Extra: if trend-aligned too, bonus reduction
                if (trendAligned) effectiveVelThreshold -= 0.03;
                entryAllowed = true;
            }
            else if (anyContActive && trendAligned)
            {
                // Tier 2: cont signal + favorable regime
                double boost = Clamp(contScore * MODE_A_TREND_BOOST_SCALE,
                    MODE_A_TREND_BOOST_MIN, MODE_A_TREND_BOOST_MAX);
                effectiveVelThreshold -= (boost + MODE_A_TREND_COMBO_FLAT);
                entryAllowed = true;
            }
            else if (anyContActive && velStrong)
            {
                // Tier 3: cont signal + strong Kalman velocity
                double boost = Clamp(contScore * MODE_A_VEL_BOOST_SCALE,
                    MODE_A_VEL_BOOST_MIN, MODE_A_VEL_BOOST_MAX);
                effectiveVelThreshold -= boost;
                entryAllowed = true;
            }
            else if (anyContActive)
            {
                // Tier 4: cont signal alone — weakest boost
                double boost = Clamp(contScore * MODE_A_SOLO_BOOST_SCALE,
                    MODE_A_SOLO_BOOST_MIN, MODE_A_SOLO_BOOST_MAX);
                effectiveVelThreshold -= boost;
                entryAllowed = true;
            }
            else
            {
                // Tier 5: classic — no cont signals, strict gate
                entryAllowed = trendAligned;
            }

            bool velOk = (isBull && vel > effectiveVelThreshold) || (!isBull && vel < -effectiveVelThreshold);

            if (entryAllowed && velOk && rev < 0.5)
            {
                if (anyContActive) ModeAWithContSignal++;
                _entryStartMs = state.Timestamp;
                string side = isBull ? "long" : "short";
                return new PolicyDecision(state.Timestamp, state.Symbol, "enter", side, "mode_a", 1.0);
            }

            // MODE B: Reversal (strict — fewer, better entries)
            bool revRegimeOk = regime == "chop"
                || (isBull && regime == "downtrend")
                || (!isBull && regime == "uptrend");
            bool velExhausted = HasVelocityDirectionChanged()
                || Math.Abs(vel) < FLAT_VELOCITY
                || (isBull && vel < 0)
                || (!isBull && vel > 0);
            bool sweepRecent = state.Timestamp - _lastSweepMs <= MODE_B_MAX_SWEEP_AGE_MS;
            bool revStrongerThanCont = rev > state.PatternSimilarity;
            bool anomalyLow = state.AnomalyScore < MODE_B_ANOMALY_MAX;

            // ── EventGrammar boost for Mode B reversal threshold ──
            // 6-tier priority system (strongest → weakest), parallel to Mode A:
            //   T1: Liq combo — liq cluster + any companion (absorption/reclaim/exhaustion)
            //   T2: Exhaustion combo — exhaustion + liq active
            //   T3: Liq standalone — liq cluster alone, gradient boost
            //   T4: Exhaustion standalone — exhaustion alone, gradient boost
            //   T5: Event combo — absorption/reclaim + liq companion
            //   T6: Event standalone — absorption/reclaim alone, gradient boost

            // Generic event (absorption/reclaim): tracked separately from exhaustion
            bool genericEventActive = _lastEventType != "" &&
                state.Timestamp - _lastEventTimestamp <= EVENT_SIGNAL_TTL_MS;

            // Exhaustion: tracked independently with its own score
            bool exhaustionActive = _lastExhaustionType != "" &&
                state.Timestamp - _lastExhaustionTimestamp <= EVENT_SIGNAL_TTL_MS;

            bool liqActive = _lastLiqType != "" &&
                state.Timestamp - _lastLiqTimestamp <= LIQ_CLUSTER_TTL_MS;

            // Combos: bidirectional detection
            bool liqComboActive = liqActive && _liqHadCompanion &&
                (genericEventActive || exhaustionActive);
            bool exhaustionComboActive = exhaustionActive && _exhaustionHadLiqCompanion && liqActive;
            bool eventComboActive = genericEventActive && _eventHadLiqCompanion && liqActive;

            double effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD;

            if (liqComboActive)
            {
                // T1: liq + companion — base 0.18 + up to 0.05 extra for high-score clusters
                double comboExtra = _liqClusterScore >= 0.85 ? MODE_B_LIQ_COMBO_EXTRA : 0.0;
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - MODE_B_LIQ_COMBO_BASE - comboExtra;
            }
            else if (exhaustionComboActive)
            {
                // T2: exhaustion + liq active — combo bonus on top of exhaustion score
                double boost = Clamp(_lastExhaustionScore * MODE_B_EXH_COMBO_SCALE,
                    MODE_B_EXH_COMBO_MIN, MODE_B_EXH_COMBO_MAX);
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - boost;
            }
            else if (liqActive)
            {
                // T3: liq standalone — score 0.5→0.13, 0.7→0.18, 1.0→0.25
                double boost = Clamp(_liqClusterScore * MODE_B_LIQ_SOLO_SCALE,
                    MODE_B_LIQ_SOLO_MIN, MODE_B_LIQ_SOLO_MAX);
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - boost;
            }
            else if (exhaustionActive)
            {
                // T4: exhaustion standalone — score 0.4→0.09, 0.6→0.13, 0.8→0.18
                double boost = Clamp(_lastExhaustionScore * MODE_B_EXH_SOLO_SCALE,
                    MODE_B_EXH_SOLO_MIN, MODE_B_EXH_SOLO_MAX);
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - boost;
            }
            else if (eventComboActive)
            {
                // T5: absorption/reclaim + liq companion
                double boost = Clamp(_lastEventScore * MODE_B_EVT_COMBO_SCALE,
                    MODE_B_EVT_COMBO_MIN, MODE_B_EVT_COMBO_MAX);
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - boost - MODE_B_EVT_COMBO_FLAT;
            }
            else if (genericEventActive)
            {
                // T6: absorption/reclaim standalone — score 0.3→0.04, 0.5→0.07, 0.8→0.11
                double boost = Clamp(_lastEventScore * MODE_B_EVT_SOLO_SCALE,
                    MODE_B_EVT_SOLO_MIN, MODE_B_EVT_SOLO_MAX);
                effectiveRevThreshold = MODE_B_REVERSAL_THRESHOLD - boost;
            }

            if (rev >= effectiveRevThreshold && velExhausted && revRegimeOk && sweepRecent && revStrongerThanCont && anomalyLow)
            {
                if (effectiveRevThreshold < MODE_B_REVERSAL_THRESHOLD) ModeBWithRevSignal++;
                _entryStartMs = state.Timestamp;
                string side = isBull ? "short" : "long"; // fade the sweep
                return new PolicyDecision(state.Timestamp, state.Symbol, "enter", side, "mode_b", 1.0);
            }
        }

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
            _momentumDecayTicks = 0; _momentumDecayStartMs = 0;
            _velocityFlipTicks = 0; _velocityFlipStartMs = 0;
            _recentVelocities.Clear();
            _patternSimHistory.Clear();
            Console.WriteLine($"PAPER ENTER {decision.Side} @ {EntryPrice:F2}");
            if (decision.Reason == "mode_a") ModeACount++;
            else if (decision.Reason == "mode_b") ModeBCount++;
            if (detector != "") LogEnter(EntryPrice, detector, similarity, velocity, tickIndex, decision.Reason);
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
            switch (decision.Reason)
            {
                case "momentum_decay": MomDecayExits++; break;
                case "velocity_flip": VelFlipExits++; break;
                case "reversal_signal": RevSigExits++; break;
                default: OtherExits++; break;
            }
            LogExit(exitPrice, decision.Reason, tickIndex);
            InPosition = false;
            PositionSide = "";
            _sweepOriginPrice = 0;
            _patternSimHistory.Clear();
            _entryStartMs = 0;
        }
    }

    public static void PrintPaperSummary()
    {
        double winRate = TotalTrades > 0 ? (double)WinningTrades / TotalTrades * 100 : 0;
        Console.WriteLine($"Summary: {TotalTrades} trades, {winRate:F0}% win, Total PnL: {TotalPnL:F2}%");
        // Mode breakdown
        var modes = _entryReasons.GroupBy(r => r)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToArray();
        if (modes.Length > 0)
            Console.WriteLine($"Modes: {string.Join(" | ", modes)}");
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
    private static readonly List<string> _detectors = new(), _exitReasons = new(), _entryReasons = new();
    private static readonly List<int> _holdingTicks = new();
    private static double _pendingEntryPrice, _pendingSimilarity, _pendingVelocity;
    private static string _pendingDetector = "";
    private static int _pendingEntryTick;
    private static bool _hasPending;

    public static void LogEnter(double entryPrice, string detector, double similarity, double velocity, int tickIndex, string entryReason = "")
    {
        _pendingEntryPrice = entryPrice; _pendingDetector = detector;
        _pendingSimilarity = similarity; _pendingVelocity = velocity;
        _pendingEntryTick = tickIndex; _hasPending = true;
        _entryReasons.Add(entryReason);
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
