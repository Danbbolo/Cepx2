# CEPx2 — Replacement Scoring Layer Design

**Date**: 2026-06-23
**Context**: DTW prototype scoring confirmed anti-predictive (high scores → losses).
**Decision**: Replace DTW with event-driven market-structure scoring.

---

## 1. Files Inspected

| File | Purpose |
|------|---------|
| `CEPx.Scoring/ScoringEngine.cs` | Current DTW scoring: Kalman + 3 prototypes + WriteState |
| `CEPx.Core/StructuralScore.cs` | Output type: PatternSimilarity, ReversalSimilarity, AnomalyScore |
| `CEPx.Core/BlackboardState.cs` | Policy input: 12 fields (SweepActive, PatternSimilarity, ReversalScore, KalmanVelocity, Regime, …) |
| `CEPx.Core/CepEvent.cs` | Event type: Timestamp, Symbol, Type, Price, Context |
| `CEPx.Core/MarketEvent.cs` | Raw tick: Timestamp, Price, Volume, BidSize, AskSize, SequenceId |
| `CEPx.EventGrammar/` | 6 detectors: Sweep, Reclaim, Absorption, Exhaustion, LiqCluster, MomentumPersistence, NoAbsorption |
| `CEPx.EventGrammar/EventGrammarConfig.cs` | ~30 configurable thresholds for all detectors |
| `CEPx.Policy/PolicyEngine.cs` | Decision layer: consumes BlackboardState.PatternSimilarity, .ReversalScore, .KalmanVelocity, .Regime, .AnomalyScore plus event signal state (6 TTL-tracked signals) |
| `CEPx.Console/Program.cs` | Candidate lifecycle: Create → Update (post-sweep) → Finalize (coherence scoring → Decide) |
| `CEPx.Scoring/PrototypeDiagnostics.cs` | 3-layer separability measurement (raw → aggregated → effective) |

---

## 2. Proposed Market Structures

Each structure has: trigger, window, features, confirm, invalidate.

### 2.1 Sweep + Reclaim (Reversal)

| Field | Value |
|-------|-------|
| **Trigger** | SweepStart (5-tick, 0.2% range) — ALREADY EXISTS |
| **Window** | 10 ticks post-sweep (current: 10m post-sweep window) |
| **Features** | (1) **Reclaim depth** — how far past origin did price reclaim? (pct of sweep range). (2) **Reclaim speed** — ticks from sweep to reclaim. (3) **Post-reclaim hold** — does price stay on the reclaimed side for ≥ 2 ticks? (4) **Wick overlap** — did the reclaim candle overlap the sweep candle's wick? |
| **Confirm** | Reclaim fires AND price holds reclaimed side ≥ 1 more tick |
| **Invalidate** | Price crosses back past sweep origin in the OPPOSITE direction (double-cross = continuation) |

**Replaces**: Current `reversal_signal` exit + Mode B reversal entry (currently uses DTW `ReversalScore` directly)

### 2.2 Breakout Attempt / Breakout Fail (Reversal)

| Field | Value |
|-------|-------|
| **Trigger** | Price breaks a prior swing high/low (configurable lookback, e.g., 20 ticks) |
| **Window** | 5 ticks post-breakout |
| **Features** | (1) **Penetration depth** — how far did it break? (2) **Close-back speed** — did it close back inside the range within 1-2 ticks? (3) **Volume on breakout** — high vol = genuine, low vol = trap. (4) **Retracement ratio** — what fraction of the breakout move was retraced? |
| **Confirm** | Price closes back inside prior range within 5 ticks |
| **Invalidate** | Breakout extends further (>2× the initial penetration) |

**Replaces**: Currently no explicit breakout-fail detector. Closest analog is sweep + reclaim but at larger scale.

### 2.3 Exhaustion After Extension (Reversal)

| Field | Value |
|-------|-------|
| **Trigger** | ExhaustionPulse / ExhaustionAfterSweep (from EventGrammar) — ALREADY EXISTS |
| **Window** | 6 ticks (current ExhaustionDetector window) |
| **Features** | (1) **Deceleration ratio** — second-half range / first-half range. (2) **Initial move magnitude** — first-half range as pct of price. (3) **Reversal lean** — does final price lean against sweep direction? (4) **Time position** — exhaustion in later ticks is higher quality. |
| **Confirm** | Deceleration ratio < 0.5 AND net move >= 0.15% |
| **Invalidate** | Next tick extends beyond first-half extreme (false exhaustion) |

**ALREADY EXISTS** in `ExhaustionDetector` — keep scoring formula, add to new score vector.

### 2.4 Absorption After Aggressive Move (Reversal)

| Field | Value |
|-------|-------|
| **Trigger** | AbsorptionAfterSweep (from EventGrammar) — ALREADY EXISTS |
| **Window** | 5 ticks (current AbsorptionDetector window) |
| **Features** | (1) **Volume intensity** — max vol / avg vol. (2) **Price stability** — price range during spike vs threshold. (3) **Volume concentration** — one tick vs spread. |
| **Confirm** | Vol >= 3× average AND price move < 0.1% during spike |
| **Invalidate** | Price breaks beyond spike range in sweep direction |

**ALREADY EXISTS** in `AbsorptionAfterSweepDetector` — keep scoring, add to score vector.

### 2.5 Liquidation Cluster at Extreme (Reversal)

| Field | Value |
|-------|-------|
| **Trigger** | LiquidationCluster (from EventGrammar) — ALREADY EXISTS |
| **Window** | 10 minutes (current LIQ_CLUSTER_TTL_MS) |
| **Features** | (1) **Count** — N liqs against sweep. (2) **Total quantity**. (3) **Whale presence** — single liq >= 20 BTC. (4) **Time concentration**. (5) **Direction ratio** — what % of liqs in window are against sweep. |
| **Confirm** | Count >= 3 AND total qty >= 10 BTC |
| **Invalidate** | No new against-sweep liqs for > TTL |

**ALREADY EXISTS** in `LiquidationClusterDetector` — keep scoring, add to score vector.

### 2.6 Continuation After Pullback (Continuation)

| Field | Value |
|-------|-------|
| **Trigger** | Sweep fades (price retraces 30-70% of sweep range) but holds above/below origin |
| **Window** | 8 ticks post-pullback |
| **Features** | (1) **Retracement depth** — how deep was the pullback? (30-50% = healthy, 70%+ = concerning). (2) **Resume speed** — how quickly does price resume sweep direction? (3) **Volume on resume** — high vol = commitment. (4) **Pullback structure** — was it a single bar or gradual? Single = stop-run, gradual = genuine absorption. |
| **Confirm** | Price resumes sweep direction AND makes a new extreme beyond sweep candle |
| **Invalidate** | Pullback continues past 70% of sweep range |

**NEW** — no current equivalent. The MomentumPersistence detector is related but measures pure momentum, not the pullback-resume pattern.

### 2.7 Momentum Persistence (Continuation)

| Field | Value |
|-------|-------|
| **Trigger** | MomentumPersistence (from EventGrammar) — ALREADY EXISTS |
| **Window** | 10 ticks |
| **Features** | (1) **Direction consistency** — % of ticks in dominant direction. (2) **Net move magnitude**. (3) **Velocity stability** — smooth vs erratic. (4) **Kalman alignment** — does Kalman agree? |
| **Confirm** | Consistency >= 60% AND net move >= 0.15% |
| **Invalidate** | A counter-tick >= 2× avg tick size (absorption spike) |

**ALREADY EXISTS** — keep scoring, add to score vector.

### 2.8 Clean Continuation (No Absorption)

| Field | Value |
|-------|-------|
| **Trigger** | NoMeaningfulAbsorption (from EventGrammar) — ALREADY EXISTS |
| **Window** | 5 ticks |
| **Features** | (1) **Price continuation** — net move. (2) **Volume containment** — max vol below absorption threshold. (3) **Smoothness** — no whipsaw stalls. |
| **Confirm** | Max vol < 2.5× avg AND net move >= 0.2% |
| **Invalidate** | An absorption spike appears |

**ALREADY EXISTS** — keep scoring, add to score vector.

### 2.9 Low-Liquidity Rejection / Acceptance (Reversal / Continuation)

| Field | Value |
|-------|-------|
| **Trigger** | SweepStart in a low-volume regime (rolling avg volume < 50% of daily avg) |
| **Window** | 8 ticks post-sweep |
| **Features** | (1) **Volume context** — how thin is the book? (2) **Wick-to-body ratio** — long wicks = rejection. (3) **Follow-through** — next 2 ticks after sweep: continue or reverse? (4) **BidSize/AskSize delta** at sweep candle (if CHD data available). |
| **Confirm** | Thin volume AND wick > 3× body in opposite direction = rejection. Thin volume AND steady follow-through = acceptance (continuation). |
| **Invalidate** | Next tick has 3× the thin-volume avg (liquidity returned) |

**NEW** — currently `BidSize`/`AskSize` fields exist in `MarketEvent` but are unused. This structure gives them a purpose when CHD data is live.

---

## 3. Per-Structure Feature Design

### 3.1 Feature Categories

| Category | Features | Source |
|----------|----------|--------|
| **Price Structure** | sweep range, reclaim depth, retracement ratio, wick ratio, close-back speed, penetration depth, resume speed | 10-tick window + sweep origin |
| **Volatility/Momentum** | Kalman velocity, deceleration ratio, initial move magnitude, direction consistency, velocity stability, net move magnitude | Kalman filter + tick-to-tick returns |
| **Volume/Liquidity** | vol intensity (max/avg), vol concentration, volume context (thin vs normal), vol on resume, BidSize/AskSize delta | MarketEvent.Volume + .BidSize/.AskSize |
| **Order Flow (if CHD)** | trade imbalance, cumulative delta, bid/ask ratio at levels | CHD futures L2 data |
| **Regime/Context** | uptrend/downtrend/chop, regime confidence, trend alignment, time-of-day | WriteState regime logic + sweep direction |

### 3.2 Feature Normalization Strategy

All features normalized to [0, 1] using configurable targets (like current EventGrammarConfig approach):

```
score = Clamp(raw / normTarget, 0, 1)
```

Configurable norm targets live in a new `ScoringConfig` class (parallel to `EventGrammarConfig`):

```csharp
public class ScoringConfig
{
    // ── Price structure norms ──
    public double ReclaimDepthNormPct { get; set; } = 0.3;     // 0.3% of price = score 1.0
    public double RetracementHealthyMax { get; set; } = 0.5;    // 50% retracement = score 1.0 for healthy
    public double PenetrationDepthNormPct { get; set; } = 0.2;
    
    // ── Volatility/Momentum norms ──
    public double KalmanVelocityNorm { get; set; } = 25.0;      // |vel| = 25 = score 1.0
    public double DecelerationNorm { get; set; } = 0.15;        // ratio 0.15 = score 1.0
    public double InitialMoveNormPct { get; set; } = 0.5;       // 0.5% = score 1.0
    
    // ── Volume/Liquidity norms ──
    public double VolIntensityNorm { get; set; } = 8.0;         // 8× avg = score 1.0
    public double ThinVolumeRatio { get; set; } = 0.5;          // <50% daily avg = thin
    
    // ── Order flow norms (CHD only, defaults for when unavailable) ──
    public double TradeImbalanceNorm { get; set; } = 0.3;       // 30% net imbalance = score 1.0
    public double CumulativeDeltaNorm { get; set; } = 100.0;
}
```

---

## 4. Replacement Scoring Architecture

### 4.1 New Output Type: `MarketStructureScore`

Replace `StructuralScore` (which carries `PatternSimilarity` + `ReversalSimilarity` as DTW outputs):

```csharp
// CEPx.Core/MarketStructureScore.cs
public readonly struct MarketStructureScore
{
    public readonly long Timestamp;
    public readonly string Symbol;
    
    // ── Kalman state (unchanged) ──
    public readonly double StateMean;
    public readonly double StateVelocity;
    public readonly double UncertaintyUpper;
    public readonly double UncertaintyLower;
    
    // ── Regime (unchanged) ──
    public readonly string Regime;           // "uptrend" / "downtrend" / "chop"
    public readonly double RegimeConfidence;  // 0.0–1.0
    
    // ── NEW: Directional conviction scores ──
    public readonly double ContinuationConviction;  // 0.0–1.0: how strong is the cont case?
    public readonly double ReversalConviction;       // 0.0–1.0: how strong is the rev case?
    
    // ── NEW: Which structures contributed ──
    public readonly StructureFlags ActiveStructures;  // bitmask
    
    // ── NEW: Individual structure scores (for diagnostics) ──
    public readonly double SweepReclaimScore;
    public readonly double ExhaustionScore;
    public readonly double AbsorptionScore;
    public readonly double LiqClusterScore;
    public readonly double MomentumPersistScore;
    public readonly double CleanContScore;
    public readonly double BreakoutFailScore;
    
    // ── Legacy compatibility (deprecated, remove after transition) ──
    public readonly double PatternSimilarity => ContinuationConviction;
    public readonly double ReversalScore => ReversalConviction;
    public readonly double AnomalyScore => 1.0 - Math.Max(ContinuationConviction, ReversalConviction);
}

[Flags]
public enum StructureFlags
{
    None             = 0,
    SweepReclaim     = 1 << 0,
    Exhaustion       = 1 << 1,
    Absorption       = 1 << 2,
    LiquidationCluster = 1 << 3,
    MomentumPersistence = 1 << 4,
    CleanContinuation  = 1 << 5,
    BreakoutFail     = 1 << 6,
    LowLiquidityReject = 1 << 7,
}
```

### 4.2 Conviction Scoring Formula

**ContinuationConviction** = weighted sum of continuation structure scores, capped at 1.0:

```
ContConviction = Clamp(
    w_mom  * MomentumPersistScore +
    w_clean * CleanContScore +
    w_resume * ContinuationAfterPullbackScore,  0, 1)
```

**ReversalConviction** = weighted sum of reversal structure scores, with combo bonus:

```
RevConviction = Clamp(
    w_reclaim * SweepReclaimScore +
    w_exh    * ExhaustionScore +
    w_abs    * AbsorptionScore +
    w_liq    * LiqClusterScore +
    w_bfail  * BreakoutFailScore +
    combo_bonus,  0, 1)
```

Where `combo_bonus` = 0.15 if ≥ 2 reversal structures are active (preserves current combo logic).

**Default weights** (tunable via ScoringConfig):

| Weight | Default | Rationale |
|--------|---------|-----------|
| w_mom | 0.35 | Momentum persistence is strongest continuation signal |
| w_clean | 0.25 | Clean move without absorption |
| w_resume | 0.15 | Pullback-resume pattern (NEW) |
| w_reclaim | 0.25 | Sweep+reclaim is the primal reversal pattern |
| w_exh | 0.20 | Exhaustion deceleration |
| w_abs | 0.20 | Absorption after aggressive move |
| w_liq | 0.15 | Liquidation cluster at extreme |
| w_bfail | 0.10 | Breakout-fail (rarer, less tested) |
| combo_bonus | 0.15 | ≥ 2 reversal structures active |

### 4.3 New Scoring Engine Interface

```csharp
// CEPx.Scoring/ScoringEngine.cs (rewritten)
public static class ScoringEngine
{
    // ── Kalman filter (unchanged, keep) ──
    public static (double price, double velocity, double upper, double lower) 
        KalmanFilter(MarketEvent[] window);
    
    // ── Regime detection (unchanged, keep) ──
    public static (string regime, double confidence) 
        DetectRegime(MarketEvent[] window);
    
    // ── NEW: Main scoring entry point ──
    public static MarketStructureScore ScoreMarket(
        MarketEvent[] priceWindow,
        CepEvent? sweep,
        CepEvent? reclaim,
        CepEvent? exhaustion,
        CepEvent? absorption,
        CepEvent? liqCluster,
        CepEvent? momentumPersist,
        CepEvent? cleanCont,
        ScoringConfig? config = null);
    
    // ── Individual structure scorers (called by ScoreMarket) ──
    public static double ScoreSweepReclaim(
        MarketEvent[] window, CepEvent sweep, CepEvent reclaim, ScoringConfig cfg);
    
    public static double ScoreBreakoutFail(
        MarketEvent[] window, double sweepOrigin, bool isBullish, ScoringConfig cfg);
    
    public static double ScoreContinuationAfterPullback(
        MarketEvent[] window, CepEvent sweep, ScoringConfig cfg);
    
    // ... etc for each structure
    
    // ── Legacy: keep for BlackboardState compatibility during transition ──
    public static BlackboardState WriteState(MarketStructureScore score, MarketEvent[] window);
    public static BlackboardState RefreshState(MarketEvent[] window, bool isBullishSweep);
}
```

### 4.4 Updated BlackboardState

Add new fields while keeping backward compatibility:

```csharp
public readonly struct BlackboardState
{
    // ── Existing (keep) ──
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly bool SweepActive;
    
    // ── REPLACED: was patternFamily string, now structure flags ──
    public readonly StructureFlags ActiveStructures;
    
    // ── REPLACED: was PatternSimilarity + ReversalScore from DTW ──
    public readonly double ContinuationConviction;
    public readonly double ReversalConviction;
    
    // ── Existing (keep) ──
    public readonly double KalmanVelocity;
    public readonly double UncertaintyUpper;
    public readonly double UncertaintyLower;
    public readonly double AnomalyScore;
    public readonly string Regime;
    public readonly double RegimeConfidence;
    public readonly string LastAction;
    
    // ── Legacy compat properties (mapped to new fields) ──
    public string PatternFamily => ActiveStructures != StructureFlags.None ? "structure" : "none";
    public double PatternSimilarity => ContinuationConviction;
    public double ReversalScore => ReversalConviction;
}
```

### 4.5 Integration with Candidate Lifecycle

The candidate lifecycle (Create → Update → Finalize → Decide) is preserved. Changes are confined to the **scoring layer only**:

**Before (current)**:
```
UpdateCandidate: reads freshState.PatternSimilarity (DTW cont) 
                and freshState.ReversalScore (DTW rev)
                → tracks peaks, averages, persistence
FinalizeCandidate: computes coherence from DTW peaks/avgs
                → feeds effectiveCont/effectiveRev into BlackboardState
                → calls Decide(state, ...)
```

**After (new)**:
```
UpdateCandidate: reads freshState.ContinuationConviction (event-driven) 
                and freshState.ReversalConviction (event-driven)
                → identical peak/avg/persistence tracking
FinalizeCandidate: computes coherence from event-driven peaks/avgs
                → feeds effectiveCont/effectiveRev into BlackboardState
                → calls Decide(state, ...)
```

**Minimal diff**: PatternSimilarity → ContinuationConviction, ReversalScore → ReversalConviction. The policy code doesn't change — it already consumes PatternSimilarity/ReversalScore/KalmanVelocity/Regime/AnomalyScore from BlackboardState.

---

## 5. Code Integration Points

### 5.1 What Gets Deleted

| File | What |
|------|------|
| `CEPx.Scoring/ScoringEngine.cs` | `SWEEP_PROTOTYPE`, `CONTINUATION_PROTOTYPE`, `REVERSAL_PROTOTYPE` arrays, `ComputeDtw()`, `ScoreEvent()` (DTW branch) |
| `CEPx.Core/StructuralScore.cs` | Replace with `MarketStructureScore.cs` |
| `CEPx.Scoring/PrototypeDiagnostics.cs` | Delete entire file (replaced by StructureDiagnostics) |

### 5.2 What Gets Created

| File | Purpose |
|------|--------|
| `CEPx.Core/MarketStructureScore.cs` | New output type with StructureFlags |
| `CEPx.Scoring/ScoringConfig.cs` | ~25 tunable normalization targets |
| `CEPx.Scoring/StructureScorers.cs` | Individual per-structure scoring functions |
| `CEPx.Scoring/StructureDiagnostics.cs` | Per-structure quality measurement (replaces ProtoDiag) |

### 5.3 What Gets Modified

| File | Change |
|------|--------|
| `CEPx.Core/BlackboardState.cs` | Add ContinuationConviction + ReversalConviction + ActiveStructures; change PatternFamily to computed property |
| `CEPx.Scoring/ScoringEngine.cs` | Remove DTW branch; add ScoreMarket() and per-structure scorers |
| `CEPx.Policy/PolicyEngine.cs` | Update CreateCandidate/UpdateCandidate to read new field names |
| `CEPx.Console/Program.cs` | Wire new scoring into post-sweep evaluation |

### 5.4 What Stays

| Component | Reason |
|-----------|--------|
| `CEPx.EventGrammar/` (all 6 detectors) | Keep — they produce CepEvents that feed the new scoring |
| `CEPx.EventGrammar/EventGrammarConfig.cs` | Keep — detection thresholds are separate from scoring norms |
| `CEPx.Policy/PolicyEngine.cs` Decide() | Keep — consumes BlackboardState fields that map 1:1 |
| Candidate lifecycle (Create/Update/Finalize) | Keep — aggregation logic works with any score type |
| `CEPx.Pipeline/` (ingestion, parsing) | Keep — data pipeline unchanged |
| Kalman filter in ScoringEngine | Keep — useful for velocity context |

---

## 6. Implementation Order

### Phase 1: Scaffold (no behavior change)

1. Create `MarketStructureScore.cs` with legacy compat properties
2. Update `BlackboardState.cs` — add new fields, computed compat props
3. Create `ScoringConfig.cs` with defaults
4. Create `StructureScorers.cs` — stub functions returning 0.0

**Test**: Build passes, 41 tests still pass (BlackboardState struct shape changes but legacy compat preserves existing behavior).

### Phase 2: Wire individual scorers

5. Implement `ScoreSweepReclaim()` — uses existing ReclaimDetector output + window
6. Implement `ScoreExhaustion()` — wraps existing ExhaustionDetector's internal scoring
7. Implement `ScoreAbsorption()` — wraps existing AbsorptionAfterSweepDetector's internal scoring
8. Implement `ScoreLiqCluster()` — wraps existing LiquidationClusterDetector's internal scoring
9. Implement `ScoreMomentumPersistence()` — wraps existing MomentumPersistenceDetector's internal scoring
10. Implement `ScoreCleanContinuation()` — wraps existing NoMeaningfulAbsorptionDetector's internal scoring

**Test**: Each scorer produces [0,1] scores. Unit tests verify >0 when signal active, =0 when absent.

### Phase 3: New structures

11. Implement `ScoreBreakoutFail()` — breakout detection + fail scoring
12. Implement `ScoreContinuationAfterPullback()` — pullback-resume pattern detection

**Test**: New structures fire on known patterns. Add 4-6 unit tests.

### Phase 4: Conviction aggregation + integration

13. Implement `ScoreMarket()` — aggregates all structure scores into Conviction values
14. Update `ScoringEngine.ScoreEvent()` to call ScoreMarket instead of DTW
15. Update `ScoringEngine.RefreshState()` / `WriteState()` to produce new BlackboardState
16. Verify PolicyEngine CreateCandidate/UpdateCandidate reads new field names

**Test**: Full integration test — replay one day, verify trades execute. Compare vs old DTW output.

### Phase 5: Diagnostics

17. Create `StructureDiagnostics.cs` — per-structure activation rates, score distributions, contribution to wins/losses
18. Wire into Program.cs

### Phase 6: Cleanup

19. Delete DTW code (prototypes, ComputeDtw, ScoreEvent DTW branch)
20. Delete PrototypeDiagnostics.cs
21. Delete StructuralScore.cs (replaced by MarketStructureScore)
22. Remove legacy compat properties from BlackboardState

---

## 7. Risks / Unknowns

| Risk | Mitigation |
|------|------------|
| **Event-driven scores are still anti-predictive** | The verdict showed DTW is anti-predictive because it measures magnitude, not direction. The new scores measure DIRECTIONAL CONFIRMATION (reclaim = reversal confirmation, momentum = continuation confirmation). If still anti-predictive, the problem is deeper than the scoring layer. Test with StructureDiagnostics. |
| **CHD L2 data may not arrive** | BidSize/AskSize currently filled with 0s in `FetchBinanceHistorical`. The LowLiquidity structures (2.9) are designed to work with Volume alone; BidSize is bonus. Make all order-flow features optional with 0-weight defaults. |
| **Too many structures = overfit** | The 8 structures have 8 weights. With 3% win rate from DTW, the risk of overfitting weights to the same losing patterns is real. Start with equal weights (all 0.15, combo bonus 0.15) and only tune after seeing > 20 winners. |
| **Aggregation (coherence scoring) preserved** | The current coherence scoring proved NEUTRAL (delta=-0.009). Keeping it is safe — it doesn't hurt. But if the new scores are cleaner, the need for coherence diminishes. Monitor rank correlation with StructureDiagnostics. |
| **Backward compat cruft** | Legacy compat properties in BlackboardState are temporary. Remove after Phase 5 diagnostics confirm new path works. |
| **Config explosion** | Already ~30 config params in EventGrammarConfig + ~25 new in ScoringConfig. Consider merging into one config class or using a YAML config file. |
