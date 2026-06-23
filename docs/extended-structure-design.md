# CEPx2 — Extended Market-Structure Framework Design

**Date**: 2026-06-23
**Context**: Sweep-centric system too narrow. Need BOS/CHoCH, pullback-resume, breakout/fail,
manipulation/liquidity sweep as first-class patterns.
**Decision**: Expand StructureFlags, add new detectors, reorganize scoring by family.

---

## 1. Files Inspected

| File | What learned |
|------|-------------|
| `CEPx.Core/CepEvent.cs` | Event type is a free-form string; no enum. All 7 detectors emit strings like "SweepStart", "Reclaim", "AbsorptionAfterSweep", "ExhaustionAfterSweep", "LiquidationCluster", "MomentumPersistence", "NoMeaningfulAbsorption" |
| `CEPx.Core/MarketStructureScore.cs` | 9 StructureFlags, 9 individual scores. All anchored to SweepReclaim as the primal pattern |
| `CEPx.Core/ActiveEventSnapshot.cs` | Carries 6 CepEvent? fields + sweep origin + direction. Sweep-anchored |
| `CEPx.EventGrammar/` (7 detectors) | All triggered by SweepStart event. No non-sweep triggers exist |
| `CEPx.Scoring/StructureScorers.cs` | 9 scorers, 6 wired to detectors, 3 window-only (return 0.0 in practice) |
| `CEPx.Console/Program.cs` | Detection loop only runs post-sweep. No pre-sweep or non-sweep analysis |
| `CEPx.Policy/PolicyEngine.cs` | Entry decision only evaluated when `state.SweepActive == true` |

## 2. Structure Families

### 2.1 Continuation Patterns

Patterns where price continues in the established direction after a move or pause.

| # | Pattern | Trigger | Confirm | Invalidate | Required Data |
|---|---------|---------|---------|------------|---------------|
| C1 | **Momentum Persistence** | Sweep with consistent tick direction | ≥60% ticks in dominant direction, net move ≥0.15% | Counter-tick ≥2× avg tick size | 10-tick window |
| C2 | **Clean Continuation** | Directional move without volume spike | Max vol < 2.5× avg, net move ≥0.2% | Absorption spike appears | 5-tick window + volume |
| C3 | **Pullback-Resume** | Price retraces 30–50% of prior range | Resumes beyond prior extreme | Retracement deepens past 70% | 10-tick window + swing level |
| C4 | **Consolidation Breakout** | Price compresses in narrow range (10+ ticks) | Breaks range with volume > avg | Fails to hold beyond range | 20-tick window + volume |
| C5 | **Trend Continuation** | Price makes higher high (uptrend) or lower low (downtrend) after pullback | HH/HL sequence intact, trend-aligned regime | Breaks prior swing low (uptrend) or high (downtrend) | 20-tick window + regime |

### 2.2 Reversal Patterns

Patterns where price reverses the established direction.

| # | Pattern | Trigger | Confirm | Invalidate | Required Data |
|---|---------|---------|---------|------------|---------------|
| R1 | **Sweep + Reclaim** | 5-tick sweep (0.2% range) + price crosses back past origin | Holds reclaimed side ≥1 tick | Double-cross past origin | Sweep window + origin |
| R2 | **Exhaustion After Extension** | Sweep with second-half range << first-half | Decel ratio < 0.5, net move ≥0.15% | Price extends beyond first-half extreme | 6-tick window |
| R3 | **Absorption After Move** | High volume bar (>3× avg) with minimal price change (<0.1%) | Price fails to continue beyond spike range | Price breaks beyond spike in original direction | 5-tick window + volume |
| R4 | **Liquidation Cluster** | ≥3 liqs against sweep direction, total qty ≥10 BTC | Whale or cascade structure | No new against-sweep liqs for >5min | LiquidationEvent[] |
| R5 | **Breakout Fail** | Price breaks swing high/low but closes back inside | Closes inside within 3 ticks | Extends 2× beyond initial penetration | 20-tick window + swing level |
| R6 | **Double Top / Bottom** | Two equal highs/lows within tolerance (0.1%) separated by ≥5 ticks | Price breaks interim swing level in opposite direction | Makes new extreme beyond the double level | 15-tick window |

### 2.3 Structure / BOS (Break of Structure)

Classic market-structure patterns: swing highs/lows, breaks, and changes of character.

| # | Pattern | Trigger | Confirm | Invalidate | Required Data |
|---|---------|---------|---------|------------|---------------|
| S1 | **BOS (Break of Structure)** | Price breaks prior swing high (bullish) or swing low (bearish) | Closes beyond level for ≥2 ticks | Closes back inside (becomes Breakout Fail) | 20-tick swing tracking |
| S2 | **CHoCH (Change of Character)** | BOS followed by immediate reversal back through the broken level | Reverses within 5 ticks of BOS | BOS extends further before reversing | 20-tick swing + BOS tracker |
| S3 | **Swing High / Low Formation** | Price makes new extreme then reverses ≥30% of prior range | Reversal holds for ≥3 ticks | New extreme in original direction | 10-tick lookback |

### 2.4 Manipulation / Liquidity Patterns

Patterns suggesting stop-hunting or liquidity engineering.

| # | Pattern | Trigger | Confirm | Invalidate | Required Data |
|---|---------|---------|---------|------------|---------------|
| M1 | **Liquidity Sweep (Stop Run)** | Price briefly breaks a clear swing level then immediately reverses | Reverses within 2 ticks, volume spike on reversal | Continues beyond level (genuine breakout) | 20-tick swing + volume |
| M2 | **Stop Hunt + Reversal** | Sweep past prior swing low (or high) + immediate reclaim | Volume spike on reclaim, trapped traders confirmed | Sweep extends >2× beyond level | Swing level + volume |
| M3 | **Low-Liquidity Rejection** | Sweep in thin volume (<50% daily avg) | Long wick opposite direction (wick > 3× body) | Follow-through in sweep direction | Volume + wick ratio |
| M4 | **Engineered Liquidity Trap** | Consolidation at swing level + false breakout + reversal | Multi-bar trap pattern (3+ bars) | Genuine breakout with volume confirmation | 10-tick window + swing |

### 2.5 Confirmation / Invalidation Signals

Meta-patterns that don't trigger entries alone but modify conviction of other patterns.

| # | Signal | What it confirms | What it invalidates |
|---|--------|-----------------|-------------------|
| X1 | **Volume Expansion** | Volume > 1.5× avg on breakout → confirms direction | Volume < 0.5× avg → fake move |
| X2 | **Regime Alignment** | Pattern direction matches regime (uptrend + long, downtrend + short) | Pattern against regime → lower conviction |
| X3 | **Time-of-Day Context** | High-volatility windows (US open, EU/London overlap) | Low-volatility windows (Asian midday) |
| X4 | **Multiple Structure Agreement** | ≥2 patterns pointing same direction | Patterns disagree → chop/no-trade |
| X5 | **Kalman Velocity** | Velocity aligned with pattern direction | Velocity flat or opposing |

## 3. Current Components → Family Mapping

| Current Component | Family | Reuse? | Notes |
|-------------------|--------|--------|-------|
| SweepDetector | R1 (Sweep+Reclaim) trigger | **Keep** | Primal trigger is still valid |
| ReclaimDetector | R1 confirm | **Keep** | Boolean check, needs scoring depth |
| ExhaustionDetector | R2 (Exhaustion) | **Keep** | Already well-scored |
| AbsorptionAfterSweepDetector | R3 (Absorption) | **Keep** | Already well-scored |
| LiquidationClusterDetector | R4 (Liq Cluster), M2 (Stop Hunt) | **Keep** | Multipurpose — works for both families |
| MomentumPersistenceDetector | C1 (Momentum) | **Keep** | Already well-scored |
| NoMeaningfulAbsorptionDetector | C2 (Clean Cont) | **Keep** | Already well-scored |
| ScoreSweepReclaim | R1 | **Keep** | Window-based, needs reclaim depth scoring |
| ScoreExhaustion | R2 | **Keep** | Faithful extraction |
| ScoreAbsorption | R3 | **Keep** | Faithful extraction |
| ScoreLiquidationCluster | R4, M2 | **Keep** | Context-parser |
| ScoreMomentumPersistence | C1 | **Keep** | Faithful extraction |
| ScoreCleanContinuation | C2 | **Keep** | Faithful extraction |
| ScoreBreakoutFail | R5 | **Keep, needs detector** | Currently returns 0.0 — needs BOS tracker |
| ScorePullbackResume | C3 | **Keep, needs detector** | Currently returns 0.0 — needs swing tracking |
| ScoreLowLiquidityReject | M3 | **Keep** | Returns 0.0 without daily avg volume context |

## 4. New Detectors Needed

### 4.1 SwingTracker (NEW — CEPx.EventGrammar)

Tracks swing highs and lows from price history. Foundation for BOS, CHoCH, Breakout Fail, and Liquidity Sweep.

```csharp
// CEPx.EventGrammar/SwingTracker.cs
public class SwingTracker
{
    // Public state (updated every tick)
    public double SwingHigh { get; private set; }
    public double SwingLow { get; private set; }
    public long SwingHighTimestamp { get; private set; }
    public long SwingLowTimestamp { get; private set; }
    
    // BOS state
    public bool BullishBOS { get; private set; }  // price broke above last swing high
    public bool BearishBOS { get; private set; }   // price broke below last swing low
    public long BOSTimestamp { get; private set; }
    
    // CHoCH state  (BOS that reversed immediately)
    public bool BullishCHoCH { get; private set; }
    public bool BearishCHoCH { get; private set; }
    
    public void Update(MarketEvent tick, int lookbackTicks = 20);
    public void Reset();
}
```

**Inputs**: MarketEvent stream (rolling), configurable lookback (default 20 ticks).
**Outputs**: SwingHigh, SwingLow, BOS direction, CHoCH direction.
**Replaces**: Manual swing-level ad-hoc logic in ScoreBreakoutFail (currently hardcoded).

### 4.2 ConsolidationDetector (NEW — CEPx.EventGrammar)

Detects price compression (narrowing range) that precedes breakout moves.

```csharp
// CEPx.EventGrammar/ConsolidationDetector.cs
public class ConsolidationDetector
{
    public static CepEvent? Detect(MarketEvent[] window, EventGrammarConfig? config = null);
    // Returns "Consolidation" event with range tightness score in context
}
```

**Inputs**: 20-tick window.
**Outputs**: CepEvent with range/avgRange ratio < 0.3 → consolidation active.

### 4.3 DoubleStructureDetector (NEW — CEPx.EventGrammar)

Detects double top / double bottom formations.

```csharp
// CEPx.EventGrammar/DoubleStructureDetector.cs
public class DoubleStructureDetector
{
    public static CepEvent? Detect(MarketEvent[] window, SwingTracker swings, 
                                    EventGrammarConfig? config = null);
    // Returns "DoubleTop" or "DoubleBottom" event with quality score
}
```

**Inputs**: 15-tick window + SwingTracker state.
**Outputs**: CepEvent with type "DoubleTop"/"DoubleBottom" and symmetry score.

### 4.4 StopHuntDetector (NEW — CEPx.EventGrammar)

Detects engineered stop runs: brief break of swing level + immediate reversal with volume.

```csharp
// CEPx.EventGrammar/StopHuntDetector.cs
public class StopHuntDetector
{
    public static CepEvent? Detect(MarketEvent[] window, SwingTracker swings,
                                    EventGrammarConfig? config = null);
    // Returns "StopHunt" event with trap quality score
}
```

**Inputs**: 10-tick window + SwingTracker state + volume.
**Outputs**: CepEvent with type "StopHunt" and trapped-volume score.

### 4.5 VolumeContextTracker (NEW — CEPx.EventGrammar)

Tracks rolling volume statistics for confirmation signals.

```csharp
// CEPx.EventGrammar/VolumeContextTracker.cs
public class VolumeContextTracker
{
    public double DailyAvgVolume { get; private set; }
    public double RecentAvgVolume { get; private set; }  // last 20 ticks
    public double VolumePercentile { get; private set; }  // current vs distribution
    
    public void Update(MarketEvent tick);
    public bool IsVolumeExpanding { get; }  // current vol > 1.5× recent avg
    public bool IsThinVolume { get; }       // current vol < 0.5× recent avg
}
```

**Inputs**: MarketEvent stream (rolling).
**Outputs**: DailyAvgVolume, volume regime (expanding/normal/thin).

## 5. Scoring Architecture by Family

### 5.1 Updated StructureFlags

```csharp
[Flags]
public enum StructureFlags
{
    None = 0,

    // ── Continuation ──
    MomentumPersistence   = 1 << 0,   // C1
    CleanContinuation     = 1 << 1,   // C2
    PullbackResume        = 1 << 2,   // C3
    ConsolidationBreakout = 1 << 3,   // C4 (NEW)
    TrendContinuation     = 1 << 4,   // C5 (NEW)

    // ── Reversal ──
    SweepReclaim          = 1 << 5,   // R1
    Exhaustion            = 1 << 6,   // R2
    Absorption            = 1 << 7,   // R3
    LiquidationCluster    = 1 << 8,   // R4
    BreakoutFail          = 1 << 9,   // R5
    DoubleStructure       = 1 << 10,  // R6 (NEW)

    // ── Structure / BOS ──
    BullishBOS            = 1 << 11,  // S1 (NEW)
    BearishBOS            = 1 << 12,  // S1 (NEW)
    BullishCHoCH          = 1 << 13,  // S2 (NEW)
    BearishCHoCH          = 1 << 14,  // S2 (NEW)

    // ── Manipulation / Liquidity ──
    StopHunt              = 1 << 15,  // M1/M2 (NEW)
    LowLiquidityReject    = 1 << 16,  // M3
    LiquidityTrap         = 1 << 17,  // M4 (NEW)

    // ── Meta (not scored, used for filtering) ──
    ConsolidationActive   = 1 << 18,  // (NEW) price compressing
    VolumeExpanding       = 1 << 19,  // (NEW) volume > 1.5× avg
    ThinVolume            = 1 << 20,  // (NEW) volume < 0.5× avg
}
```

### 5.2 Family-Based Conviction Scoring

Instead of flat weights, group by family with inter-family bonuses:

```csharp
// ContinuationConviction = weighted family scores
ContConviction = Clamp01(
    FAMILY_CONT   * 0.40 +   // C1–C5: best continuation score
    FAMILY_BOS    * 0.30 +   // S1: BOS aligned with position
    FAMILY_META   * 0.30);   // X1–X5: volume/regime aligned

// ReversalConviction = weighted family scores
RevConviction = Clamp01(
    FAMILY_REV    * 0.50 +   // R1–R6: best reversal score
    FAMILY_MANIP  * 0.30 +   // M1–M4: manipulation confirmation
    FAMILY_META   * 0.20);   // X1–X5: volume/regime aligned

// Where FAMILY_X = max(score in that family) if ≥1 structure active, else 0
```

**Combo bonus**: ≥1 structure from two different families → +0.10 (cross-family confirmation).

### 5.3 Updated ActiveEventSnapshot

```csharp
public readonly struct ActiveEventSnapshot
{
    // ── Sweep context (unchanged) ──
    public readonly double SweepOrigin;
    public readonly bool IsBullishSweep;
    public readonly double KalmanVelocity;
    
    // ── All detector outputs (expanded) ──
    public readonly CepEvent? Reclaim;
    public readonly CepEvent? Exhaustion;
    public readonly CepEvent? Absorption;
    public readonly CepEvent? LiquidationCluster;
    public readonly CepEvent? MomentumPersistence;
    public readonly CepEvent? CleanContinuation;
    
    // ── NEW: BOS/Structure events ──
    public readonly CepEvent? BullishBOS;
    public readonly CepEvent? BearishBOS;
    public readonly CepEvent? BullishCHoCH;
    public readonly CepEvent? BearishCHoCH;
    
    // ── NEW: Manipulation events ──
    public readonly CepEvent? StopHunt;
    public readonly CepEvent? Consolidation;
    public readonly CepEvent? DoubleStructure;
    public readonly CepEvent? LiquidityTrap;
    
    // ── NEW: Volume context ──
    public readonly double RecentAvgVolume;
    public readonly double DailyAvgVolume;
    public readonly bool IsVolumeExpanding;
    public readonly bool IsThinVolume;
    
    // ── NEW: Swing state ──
    public readonly double SwingHigh;
    public readonly double SwingLow;
}
```

### 5.4 Updated ScoreMarket()

```csharp
public static MarketStructureScore ScoreMarket(
    MarketEvent[] priceWindow,
    ActiveEventSnapshot events,
    ScoringConfig? config = null)
{
    // Phase 1: Call all structure scorers (16 total)
    //   - 5 continuation scorers (C1–C5)
    //   - 6 reversal scorers (R1–R6)
    //   - 4 manipulation scorers (M1–M4)
    //   - 4 BOS/CHoCH scorers (S1–S2)

    // Phase 2: Group by family
    double familyCont = Max(C1–C5 scores);
    double familyRev  = Max(R1–R6 scores);
    double familyBOS  = Max(S1 scores aligned with direction);
    double familyManip = Max(M1–M4 scores);
    
    // Phase 3: Apply meta-filters (volume, regime, time-of-day)
    double metaScore = ComputeMetaScore(events);
    
    // Phase 4: Compute convictions with family weights
    double contConviction = Clamp01(familyCont * wCont + familyBOS * wBOS + metaScore * wMeta);
    double revConviction  = Clamp01(familyRev * wRev + familyManip * wManip + metaScore * wMeta);
    
    // Phase 5: Combo bonus (cross-family)
    if (familyRev > 0 && familyManip > 0) revConviction += 0.10;
    if (familyCont > 0 && familyBOS > 0)  contConviction += 0.10;
    
    return new MarketStructureScore(/* all 16 scores populated */);
}
```

## 6. Implementation Order

### Phase A: Foundation (swing + volume tracking)

| Step | Component | Dependency |
|------|-----------|------------|
| A1 | `SwingTracker` class | None — new file |
| A2 | `VolumeContextTracker` class | None — new file |
| A3 | Integrate into Program.cs main loop (call Update each tick) | A1, A2 |
| A4 | Add swing/volume fields to `ActiveEventSnapshot` | A1, A2 |
| **Test**: Build, verify swing levels tracked. No behavior change. | | |

### Phase B: New detectors

| Step | Component | Dependency |
|------|-----------|------------|
| B1 | `ConsolidationDetector` | A1 (swing levels for range) |
| B2 | `DoubleStructureDetector` | A1 (swing levels for equality check) |
| B3 | `StopHuntDetector` | A1 (swing levels for sweep point) |
| B4 | Wire new detectors into Program.cs detection loop | B1–B3 |
| **Test**: Each detector fires on known patterns. Unit tests. | | |

### Phase C: New scorers

| Step | Component | Dependency |
|------|-----------|------------|
| C1 | `ScoreConsolidationBreakout()` (C4) | B1 |
| C2 | `ScoreTrendContinuation()` (C5) | A1 (swing for HH/HL) |
| C3 | `ScoreDoubleStructure()` (R6) | B2 |
| C4 | `ScoreBOS()` (S1) | A1 |
| C5 | `ScoreCHoCH()` (S2) | A1 + C4 |
| C6 | `ScoreStopHunt()` (M1/M2) | B3 |
| C7 | `ScoreLiquidityTrap()` (M4) | B1 + B3 |
| C8 | `ComputeMetaScore()` (X1–X5) | A2 |
| **Test**: Each scorer returns [0,1]. Integration test with full window. | | |

### Phase D: Family-based scoring

| Step | Component | Dependency |
|------|-----------|------------|
| D1 | Update `StructureFlags` enum (21 flags) | — |
| D2 | Update `MarketStructureScore` (16 score fields) | D1 |
| D3 | Rewrite `ScoreMarket()` with family grouping + meta-filters | C1–C8 |
| D4 | Update `ScoringConfig` with family weights | D3 |
| **Test**: Full 7-day replay. Compare to pre-Phase A baseline. | | |

### Phase E: Non-sweep triggers

| Step | Component | Dependency |
|------|-----------|------------|
| E1 | Add non-sweep entry path in Program.cs (BOS triggers, consolidation triggers) | D3 |
| E2 | Update PolicyEngine entry gate to accept non-sweep candidates | E1 |
| E3 | Update candidate lifecycle for non-sweep triggers | E2 |
| **Test**: Trade count increases (more triggers). Win rate monitored. | | |

### Phase F: Cleanup

| Step | Component | Dependency |
|------|-----------|------------|
| F1 | Remove DTW code (prototypes, ComputeDtw) | E3 |
| F2 | Remove old StructuralScore | F1 |
| F3 | Remove sweep-only constraint from PolicyEngine | F1 |

## 7. Risks / Unknowns

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Non-sweep triggers flood entries** | HIGH | Phase E adds triggers but same coherence scoring applies. Gate via meta-filters (volume, regime). If trade count >500/day, tighten. |
| **SwingTracker false signals in chop** | MEDIUM | Require minimum swing range (0.05% of price) and time separation (≥5 ticks between swing points). |
| **CHoCH vs Breakout Fail overlap** | LOW | CHoCH = BOS that reverses within 5 ticks. Breakout Fail = break that closes back inside. CHoCH is a subset. Both useful in different contexts. |
| **Volume context from 1m candles** | LOW | VolumeContextTracker uses MarketEvent.Volume which is populated from Binance/CHD. No L2 needed. |
| **Family weights overfitting** | MEDIUM | Start with equal family weights (0.33 each). Only tune after ≥50 winners across ≥14 days. |
| **StructureFlags enum too large** | LOW | 21 flags is manageable. Group by family in output. Can always split into separate enums per family if needed. |
| **SweepDetector still primal trigger** | MEDIUM | Phase E adds non-sweep triggers but sweep is still the fastest signal. BOS/Consolidation triggers fire less often. Design allows both paths. |
| **Compatibility with candidate lifecycle** | LOW | ActiveEventSnapshot carries all state. Candidate lifecycle (Create→Update→Finalize) unchanged. Only the trigger source expands. |
