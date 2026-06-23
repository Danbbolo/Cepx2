using System.Globalization;

namespace CEPx.Scoring;

/// <summary>
/// Prototype-discrimination diagnostics.
/// Measures whether the DTW prototype scoring layer (continuation similarity vs
/// reversal similarity) can separate good setups from bad setups.
///
/// LIFE CYCLE: Instantiate once per replay. Feed every candidate at finalization.
/// Feed trade outcomes at exit. Call PrintSummary() at end.
///
/// REMOVABLE: Delete this file and the three feed calls in PolicyEngine/Program
/// when analysis is complete.
/// </summary>
public class PrototypeDiagnostics
{
    /// <summary>Singleton instance for cross-layer access (Scoring→Diagnostics without Policy dependency).</summary>
    public static PrototypeDiagnostics Instance { get; set; } = new();

    private readonly List<CandidateRecord> _candidates = new();
    private int _enteredIndex = -1; // index of the currently open candidate in _candidates
    private int _candidateSeq;      // monotonically increasing candidate id

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Feed a finalized candidate (before entry/paper execution).</summary>
    public void RecordCandidate(
        int createdTick,
        double peakCont, double avgCont, double contPersistence,
        double peakRev, double avgRev, double revPersistence,
        double conflictOverlap,
        int revSignalCount, int contSignalCount,
        double effectiveCont, double effectiveRev,
        string finalOutcome, string noopReason,
        string triggerSource = "sweep")
    {
        var rec = new CandidateRecord
        {
            Id = _candidateSeq++,
            CreatedTick = createdTick,
            PeakContScore = peakCont,
            AvgContScore = avgCont,
            ContPersistence = contPersistence,
            PeakRevScore = peakRev,
            AvgRevScore = avgRev,
            RevPersistence = revPersistence,
            ConflictOverlap = conflictOverlap,
            RevSignalCount = revSignalCount,
            ContSignalCount = contSignalCount,
            EffectiveContScore = effectiveCont,
            EffectiveRevScore = effectiveRev,
            FinalOutcome = finalOutcome,
            NoopReason = noopReason,
            TriggerSource = triggerSource,
            FamilyContScore = _pendingFamilyCont,
            FamilyRevScore = _pendingFamilyRev,
            FamilyBOSScore = _pendingFamilyBOS,
            FamilyManipScore = _pendingFamilyManip,
            MetaScore = _pendingMeta,
            PnL = 0,
            IsWin = false,
            ExitReason = "",
            HasTradeOutcome = false
        };
        _candidates.Add(rec);
        if (finalOutcome is "mode_a" or "mode_b")
            _enteredIndex = _candidates.Count - 1;
    }

    // ── Pending family scores (populated by ScoreMarket, consumed by RecordCandidate) ──
    private double _pendingFamilyCont, _pendingFamilyRev, _pendingFamilyBOS, _pendingFamilyManip, _pendingMeta;

    /// <summary>Feed family scores from the most recent ScoreMarket call.</summary>
    public void RecordFamilyScores(double familyCont, double familyRev, double familyBOS,
        double familyManip, double meta)
    {
        _pendingFamilyCont = familyCont;
        _pendingFamilyRev = familyRev;
        _pendingFamilyBOS = familyBOS;
        _pendingFamilyManip = familyManip;
        _pendingMeta = meta;
    }

    /// <summary>Feed the trade result after exit. Matches the most recent entered candidate.</summary>
    public void RecordTradeOutcome(double pnl, bool isWin, string exitReason)
    {
        if (_enteredIndex < 0 || _enteredIndex >= _candidates.Count) return;
        var rec = _candidates[_enteredIndex];
        rec.PnL = pnl;
        rec.IsWin = isWin;
        rec.ExitReason = exitReason;
        rec.HasTradeOutcome = true;
        _candidates[_enteredIndex] = rec;
        _enteredIndex = -1;
    }

    /// <summary>Print the full discrimination summary to Console.</summary>
    public void PrintSummary()
    {
        int n = _candidates.Count;
        if (n == 0)
        {
            Console.WriteLine("\n=== PROTOTYPE DISCRIMINATION ===\n  No candidates.");
            return;
        }

        var entered = _candidates.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
        var noops = _candidates.Where(c => c.FinalOutcome == "noop").ToList();
        var modeA = _candidates.Where(c => c.FinalOutcome == "mode_a").ToList();
        var modeB = _candidates.Where(c => c.FinalOutcome == "mode_b").ToList();
        var winners = entered.Where(c => c.IsWin).ToList();
        var losers = entered.Where(c => !c.IsWin).ToList();

        Console.WriteLine();
        Console.WriteLine("=== PROTOTYPE DISCRIMINATION DIAGNOSTICS ===");
        Console.WriteLine($"  Total candidates: {n}");
        Console.WriteLine($"    Entered: {entered.Count}  (mode_a={modeA.Count}  mode_b={modeB.Count})");
        Console.WriteLine($"    Noop:    {noops.Count}");
        // Trigger source breakdown
        int sweepCand = _candidates.Count(c => c.TriggerSource == "sweep");
        int bosCand = _candidates.Count(c => c.TriggerSource == "bos");
        int consolCand = _candidates.Count(c => c.TriggerSource == "consolidation");
        int pbCand = _candidates.Count(c => c.TriggerSource == "pullback");
        Console.WriteLine($"    Trigger: sweep={sweepCand} bos={bosCand} consolidation={consolCand} pullback={pbCand}");
        int sweepEntered = entered.Count(c => c.TriggerSource == "sweep");
        int nonSweepEntered = entered.Count(c => c.TriggerSource != "sweep");
        Console.WriteLine($"    Entered by trigger: sweep={sweepEntered} non-sweep={nonSweepEntered}");

        // ── 1. Score distributions by outcome ─────────────────────
        PrintScoreDistributions("Continuation Score", entered, noops, modeA, modeB,
            c => c.PeakContScore);
        PrintScoreDistributions("Reversal Score", entered, noops, modeA, modeB,
            c => c.PeakRevScore);

        // ── 2. Winners vs losers score comparison ─────────────────
        if (winners.Count > 0 && losers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("-- Winners vs Losers --");
            Console.WriteLine($"  Winners: {winners.Count}  Losers: {losers.Count}");

            PrintWinnerLoserStat("Peak Cont", winners, losers, c => c.PeakContScore);
            PrintWinnerLoserStat("Avg Cont", winners, losers, c => c.AvgContScore);
            PrintWinnerLoserStat("Peak Rev", winners, losers, c => c.PeakRevScore);
            PrintWinnerLoserStat("Avg Rev", winners, losers, c => c.AvgRevScore);
            PrintWinnerLoserStat("Cont Persist", winners, losers, c => c.ContPersistence);
            PrintWinnerLoserStat("Rev Persist", winners, losers, c => c.RevPersistence);
            PrintWinnerLoserStat("Conflict", winners, losers, c => c.ConflictOverlap);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("-- Winners vs Losers --");
            Console.WriteLine($"  Insufficient data: {winners.Count} winners, {losers.Count} losers");
        }

        // ── 3. Continuation vs Reversal trade score comparison ───
        if (modeA.Count > 0 && modeB.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("-- Mode A (Cont) vs Mode B (Rev) Trade Scores --");
            PrintStatLine("Peak Cont", modeA, modeB, c => c.PeakContScore);
            PrintStatLine("Peak Rev", modeA, modeB, c => c.PeakRevScore);
        }

        // ── 4. Correlation between continuation and reversal scores ──
        double pearsonR = ComputePearson(
            _candidates.Select(c => c.PeakContScore).ToArray(),
            _candidates.Select(c => c.PeakRevScore).ToArray());
        Console.WriteLine();
        Console.WriteLine("-- Cont/Rev Score Correlation --");
        Console.WriteLine($"  Pearson r (all candidates): {pearsonR:F4}");
        if (entered.Count > 0)
        {
            double pearsonEntered = ComputePearson(
                entered.Select(c => c.PeakContScore).ToArray(),
                entered.Select(c => c.PeakRevScore).ToArray());
            Console.WriteLine($"  Pearson r (entered only):   {pearsonEntered:F4}");
        }

        // ── 5. Co-movement: how often both scores rise together ──
        double medCont = Median(_candidates.Select(c => c.PeakContScore).ToArray());
        double medRev = Median(_candidates.Select(c => c.PeakRevScore).ToArray());
        int bothHigh = _candidates.Count(c => c.PeakContScore >= medCont && c.PeakRevScore >= medRev);
        int bothLow = _candidates.Count(c => c.PeakContScore < medCont && c.PeakRevScore < medRev);
        int contHighRevLow = _candidates.Count(c => c.PeakContScore >= medCont && c.PeakRevScore < medRev);
        int contLowRevHigh = _candidates.Count(c => c.PeakContScore < medCont && c.PeakRevScore >= medRev);
        Console.WriteLine();
        Console.WriteLine("-- Score Co-movement (above/below median) --");
        Console.WriteLine($"  Median cont: {medCont:F3}  Median rev: {medRev:F3}");
        Console.WriteLine($"  Both high:  {bothHigh,4} ({100.0*bothHigh/n:F1}%)");
        Console.WriteLine($"  Both low:   {bothLow,4} ({100.0*bothLow/n:F1}%)");
        Console.WriteLine($"  Cont hi/Rev lo: {contHighRevLow,4} ({100.0*contHighRevLow/n:F1}%)");
        Console.WriteLine($"  Cont lo/Rev hi: {contLowRevHigh,4} ({100.0*contLowRevHigh/n:F1}%)");
        if (bothHigh + bothLow > contHighRevLow + contLowRevHigh)
            Console.WriteLine($"  → Scores MOVE TOGETHER more often than they diverge ({(bothHigh+bothLow)} vs {contHighRevLow+contLowRevHigh})");
        else
            Console.WriteLine($"  → Scores DIVERGE more often than they move together");

        // ── 6. High-score failure rate ────────────────────────────
        var highContEntered = entered.Where(c => c.PeakContScore >= 0.50).ToList();
        var highRevEntered = entered.Where(c => c.PeakRevScore >= 0.40).ToList();
        Console.WriteLine();
        Console.WriteLine("-- High-Score Failure Rate --");
        if (highContEntered.Count > 0)
        {
            int highContLost = highContEntered.Count(c => !c.IsWin);
            Console.WriteLine($"  PeakCont >= 0.50 entered: {highContEntered.Count}  lost: {highContLost} ({100.0*highContLost/highContEntered.Count:F0}%)");
        }
        else
            Console.WriteLine($"  PeakCont >= 0.50 entered: 0");
        if (highRevEntered.Count > 0)
        {
            int highRevLost = highRevEntered.Count(c => !c.IsWin);
            Console.WriteLine($"  PeakRev  >= 0.40 entered: {highRevEntered.Count}  lost: {highRevLost} ({100.0*highRevLost/highRevEntered.Count:F0}%)");
        }
        else
            Console.WriteLine($"  PeakRev  >= 0.40 entered: 0");

        // ── 7. Conflict pattern analysis ──────────────────────────
        var conflictCandidates = _candidates.Where(c => c.ConflictOverlap >= 0.30).ToList();
        var conflictEntered = conflictCandidates.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
        Console.WriteLine();
        Console.WriteLine("-- Conflict (Overlap >= 0.30) --");
        Console.WriteLine($"  Conflict candidates: {conflictCandidates.Count}/{n} ({100.0*conflictCandidates.Count/n:F1}%)");
        if (conflictEntered.Count > 0)
        {
            int conflictLost = conflictEntered.Count(c => !c.IsWin);
            Console.WriteLine($"  Conflict entered:    {conflictEntered.Count}  lost: {conflictLost} ({100.0*conflictLost/conflictEntered.Count:F0}%)");
        }

        // ── 8. Noop reason breakdown ─────────────────────────────
        var noopReasons = noops.GroupBy(c => string.IsNullOrEmpty(c.NoopReason) ? "(none)" : c.NoopReason)
            .OrderByDescending(g => g.Count()).ToList();
        if (noopReasons.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("-- Noop Reason Breakdown --");
            foreach (var g in noopReasons)
                Console.WriteLine($"  {g.Key}: {g.Count()}");
        }

        // ── 9. Separability verdict ──────────────────────────────
        Console.WriteLine();
        Console.WriteLine("-- Raw Prototype Separability (Layer 1: DTW only) --");
        double rawSep = SeparabilityScore(winners, losers,
            c => c.PeakContScore, c => c.PeakRevScore);
        PrintSepLine("Raw (peak)", rawSep);

        Console.WriteLine();
        Console.WriteLine("-- Aggregated Separability (Layer 2: avg + persistence) --");
        double aggSep = SeparabilityScore(winners, losers,
            c => c.AvgContScore, c => c.AvgRevScore);
        PrintSepLine("Aggregated (avg)", aggSep);

        Console.WriteLine();
        Console.WriteLine("-- Effective Separability (Layer 3: coherence + bonus) --");
        double effSep = SeparabilityScore(winners, losers,
            c => c.EffectiveContScore, c => c.EffectiveRevScore);
        PrintSepLine("Effective (final)", effSep);

        // ── 10. Before/after: does aggregation help or hurt? ──────
        Console.WriteLine();
        Console.WriteLine("-- Aggregation Impact (Raw → Aggregated → Effective) --");
        Console.WriteLine($"  Raw peak separability:      {rawSep:F4}");
        Console.WriteLine($"  Aggregated separability:    {aggSep:F4}");
        Console.WriteLine($"  Effective separability:     {effSep:F4}");
        double aggregationDelta = aggSep - rawSep;
        double fullDelta = effSep - rawSep;
        string aggVerdict = aggregationDelta > 0.02 ? "IMPROVES" : aggregationDelta < -0.02 ? "DEGRADES" : "NEUTRAL";
        string fullVerdict = fullDelta > 0.02 ? "IMPROVES" : fullDelta < -0.02 ? "DEGRADES" : "NEUTRAL";
        Console.WriteLine($"  Aggregation delta:  {aggregationDelta:+0.0000;-0.0000; 0.0000} → aggregation {aggVerdict} signal");
        Console.WriteLine($"  Full pipeline delta: {fullDelta:+0.0000;-0.0000; 0.0000} → full pipeline {fullVerdict} signal");

        // Raw vs aggregated correlation: does aggregation preserve ranking?
        var enteredArr = entered.ToArray();
        if (enteredArr.Length >= 5)
        {
            double rankCorrCont = SpearmanRank(enteredArr.Select(c => c.PeakContScore).ToArray(),
                                               enteredArr.Select(c => c.EffectiveContScore).ToArray());
            double rankCorrRev = SpearmanRank(enteredArr.Select(c => c.PeakRevScore).ToArray(),
                                              enteredArr.Select(c => c.EffectiveRevScore).ToArray());
            Console.WriteLine($"  Rank correlation (raw→eff cont): {rankCorrCont:F4}");
            Console.WriteLine($"  Rank correlation (raw→eff rev):  {rankCorrRev:F4}");
            if (rankCorrCont < 0.7 || rankCorrRev < 0.7)
                Console.WriteLine($"  WARNING: Aggregation is REORDERING candidates (rank corr < 0.7)");
            else
                Console.WriteLine($"  Aggregation preserves ranking order (rank corr >= 0.7)");
        }

        // ── 11. Layer-by-layer winner/loser score comparison ──────
        if (winners.Count >= 2 && losers.Count >= 2)
        {
            Console.WriteLine();
            Console.WriteLine("-- Layer-by-Layer Winner/Loser Score Comparison --");
            Console.WriteLine($"  {"Metric",-20} {"Winners",8} {"Losers",8} {"Diff",8} {"Sep?",6}");
            Console.WriteLine(new string('-', 55));

            PrintLayerComparison("Raw Peak Cont", winners, losers, c => c.PeakContScore);
            PrintLayerComparison("Raw Peak Rev", winners, losers, c => c.PeakRevScore);
            PrintLayerComparison("Agg Avg Cont", winners, losers, c => c.AvgContScore);
            PrintLayerComparison("Agg Avg Rev", winners, losers, c => c.AvgRevScore);
            PrintLayerComparison("Eff Cont", winners, losers, c => c.EffectiveContScore);
            PrintLayerComparison("Eff Rev", winners, losers, c => c.EffectiveRevScore);
            PrintLayerComparison("Cont Persist", winners, losers, c => c.ContPersistence);
            PrintLayerComparison("Rev Persist", winners, losers, c => c.RevPersistence);
        }

        // ── 12. FINAL VERDICT ─────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("=== DIAGNOSTICS & RECALIBRATION ===");
        Console.WriteLine("============================================================");

        // ── 13. Score distribution summary (key percentiles) ──────
        Console.WriteLine();
        Console.WriteLine("-- Score Distribution Summary (all candidates) --");
        PrintPercentileTable("  Cont", _candidates.Select(c => c.PeakContScore).ToArray());
        PrintPercentileTable("  Rev ", _candidates.Select(c => c.PeakRevScore).ToArray());
        if (entered.Count > 0)
        {
            Console.WriteLine("-- Score Distribution Summary (entered only) --");
            PrintPercentileTable("  Cont", entered.Select(c => c.PeakContScore).ToArray());
            PrintPercentileTable("  Rev ", entered.Select(c => c.PeakRevScore).ToArray());
        }

        // ── 13b. Family score distributions ───────────────────────
        Console.WriteLine();
        Console.WriteLine("-- Family Score Distributions (all candidates) --");
        PrintPercentileTable("  famCont ", _candidates.Select(c => c.FamilyContScore).ToArray());
        PrintPercentileTable("  famRev  ", _candidates.Select(c => c.FamilyRevScore).ToArray());
        PrintPercentileTable("  famBOS  ", _candidates.Select(c => c.FamilyBOSScore).ToArray());
        PrintPercentileTable("  famManip", _candidates.Select(c => c.FamilyManipScore).ToArray());
        PrintPercentileTable("  meta    ", _candidates.Select(c => c.MetaScore).ToArray());

        // ── 13c. Family contribution to winners vs losers ─────────
        if (winners.Count >= 2 && losers.Count >= 2)
        {
            Console.WriteLine();
            Console.WriteLine("-- Family Contribution: Winners vs Losers (entered only) --");
            Console.WriteLine($"  {"Family",-12} {"Winners",8} {"Losers",8} {"Diff",8} {"Help/Hurt",10}");
            Console.WriteLine(new string('-', 50));
            PrintFamilyContribution("famCont", winners, losers, c => c.FamilyContScore);
            PrintFamilyContribution("famRev", winners, losers, c => c.FamilyRevScore);
            PrintFamilyContribution("famBOS", winners, losers, c => c.FamilyBOSScore);
            PrintFamilyContribution("famManip", winners, losers, c => c.FamilyManipScore);
            PrintFamilyContribution("meta", winners, losers, c => c.MetaScore);
        }

        // ── 13d. Meta-score filter analysis ───────────────────────
        Console.WriteLine();
        Console.WriteLine("-- Meta-Score Filter Analysis --");
        double medMeta = Median(_candidates.Select(c => c.MetaScore).ToArray());
        var highMetaCandidates = _candidates.Where(c => c.MetaScore >= medMeta).ToList();
        var lowMetaCandidates = _candidates.Where(c => c.MetaScore < medMeta).ToList();
        var highMetaEntered = highMetaCandidates.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
        var lowMetaEntered = lowMetaCandidates.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
        Console.WriteLine($"  Meta median: {medMeta:F3}");
        Console.WriteLine($"  High meta (≥{medMeta:F2}): {highMetaCandidates.Count} candidates → {highMetaEntered.Count} entered, " +
            $"{highMetaEntered.Count(c => c.IsWin)} wins ({100.0*highMetaEntered.Count(c=>c.IsWin)/Math.Max(1,highMetaEntered.Count):F0}%)");
        Console.WriteLine($"  Low meta  (<{medMeta:F2}): {lowMetaCandidates.Count} candidates → {lowMetaEntered.Count} entered, " +
            $"{lowMetaEntered.Count(c => c.IsWin)} wins ({100.0*lowMetaEntered.Count(c=>c.IsWin)/Math.Max(1,lowMetaEntered.Count):F0}%)");
        if (highMetaEntered.Count > 0 && lowMetaEntered.Count > 0)
        {
            double highWinRate = (double)highMetaEntered.Count(c => c.IsWin) / highMetaEntered.Count;
            double lowWinRate = (double)lowMetaEntered.Count(c => c.IsWin) / lowMetaEntered.Count;
            Console.WriteLine(highWinRate > lowWinRate
                ? $"  → Meta score HELPS: high-meta candidates win more ({highWinRate:P0} vs {lowWinRate:P0})"
                : $"  → Meta score HURTS or is neutral: high-meta wins {highWinRate:P0} vs low-meta {lowWinRate:P0}");
        }

        // ── 14. Threshold sweep ──────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("-- Threshold Sweep (Continuation) --");
        Console.WriteLine($"  {"Thresh",8} {"Pass",6} {"Entered",8} {"Wins",6} {"Win%",6} {"AvgPnL",8}");
        Console.WriteLine(new string('-', 50));
        foreach (double t in new[] { 0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50 })
        {
            var pass = _candidates.Where(c => c.PeakContScore >= t).ToList();
            var passEntered = pass.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
            int pw = passEntered.Count(c => c.IsWin);
            double pwr = passEntered.Count > 0 ? (double)pw / passEntered.Count * 100 : 0;
            double pPnl = passEntered.Count > 0 ? passEntered.Average(c => c.PnL) : 0;
            Console.WriteLine($"  {t,8:F2} {pass.Count,6} {passEntered.Count,8} {pw,6} {pwr,5:F1}% {pPnl,8:F2}%");
        }

        Console.WriteLine();
        Console.WriteLine("-- Threshold Sweep (Reversal) --");
        Console.WriteLine($"  {"Thresh",8} {"Pass",6} {"Entered",8} {"Wins",6} {"Win%",6} {"AvgPnL",8}");
        Console.WriteLine(new string('-', 50));
        foreach (double t in new[] { 0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50 })
        {
            var pass = _candidates.Where(c => c.PeakRevScore >= t).ToList();
            var passEntered = pass.Where(c => c.FinalOutcome is "mode_a" or "mode_b").ToList();
            int pw = passEntered.Count(c => c.IsWin);
            double pwr = passEntered.Count > 0 ? (double)pw / passEntered.Count * 100 : 0;
            double pPnl = passEntered.Count > 0 ? passEntered.Average(c => c.PnL) : 0;
            Console.WriteLine($"  {t,8:F2} {pass.Count,6} {passEntered.Count,8} {pw,6} {pwr,5:F1}% {pPnl,8:F2}%");
        }

        // ── 15. Structure activation frequencies ─────────────────
        Console.WriteLine();
        Console.WriteLine("-- Structure Activation Frequencies (from signal counts) --");
        int totalSigCandidates = _candidates.Count;
        PrintActivation("Exhaustion", _candidates.Count(c => c.RevSignalCount > 0 && _candidateHadExhaustion(c)), totalSigCandidates);
        PrintActivation("Absorption/Reclaim", _candidates.Count(c => c.RevSignalCount > 0), totalSigCandidates);
        PrintActivation("LiqCluster", _candidates.Count(c => c.ContSignalCount >= 0 &&
            c.RevSignalCount > 0), totalSigCandidates); // approximate
        PrintActivation("MomentumPersistence", _candidates.Count(c => c.ContSignalCount > 0), totalSigCandidates);
        PrintActivation("CleanContinuation", _candidates.Count(c => c.ContSignalCount > 0), totalSigCandidates);

        // ── 16. Current policy constants vs observed scores ───────
        Console.WriteLine();
        Console.WriteLine("-- Policy Constant Calibration Check --");
        double medContAll = Median(_candidates.Select(c => c.PeakContScore).ToArray());
        double medRevAll = Median(_candidates.Select(c => c.PeakRevScore).ToArray());
        double medContEntered = entered.Count > 0
            ? Median(entered.Select(c => c.PeakContScore).ToArray()) : 0;
        double medRevEntered = entered.Count > 0
            ? Median(entered.Select(c => c.PeakRevScore).ToArray()) : 0;
        double p90Cont = Percentile(_candidates.Select(c => c.PeakContScore).ToArray(), 0.90);
        double p90Rev = Percentile(_candidates.Select(c => c.PeakRevScore).ToArray(), 0.90);

        Console.WriteLine($"  Observed scores:");
        Console.WriteLine($"    Cont: median={medContAll:F3} (all) {medContEntered:F3} (entered)  p90={p90Cont:F3}");
        Console.WriteLine($"    Rev:  median={medRevAll:F3} (all) {medRevEntered:F3} (entered)  p90={p90Rev:F3}");
        Console.WriteLine();
        Console.WriteLine($"  Current PolicyEngine constants:");
        Console.WriteLine($"    SIMILARITY_THRESHOLD (cont persist):  0.35");
        Console.WriteLine($"    MODE_B_REVERSAL_THRESHOLD (rev entry): 0.32");
        Console.WriteLine($"    ENTRY_VELOCITY_THRESHOLD:              0.50");
        Console.WriteLine();

        // Recommend adjustments
        Console.WriteLine("  Recommendation:");
        string contAdvice = medContAll < 0.20
            ? "Cont scores are VERY LOW. Threshold 0.35 is too high — most candidates fail."
            : medContAll < 0.30
            ? "Cont scores are LOW. Consider lowering SIMILARITY_THRESHOLD to 0.20-0.25."
            : "Cont scores in normal range. Threshold 0.35 may be appropriate.";
        Console.WriteLine($"    {contAdvice}");

        string revAdvice = medRevAll < 0.15
            ? "Rev scores are VERY LOW. Threshold 0.32 is too high — consider 0.15-0.20."
            : medRevAll < 0.25
            ? "Rev scores are LOW. Consider lowering MODE_B_REVERSAL_THRESHOLD to 0.20-0.25."
            : "Rev scores in normal range.";
        Console.WriteLine($"    {revAdvice}");

        // ── 17. High-score test with NEW thresholds ──────────────
        Console.WriteLine();
        Console.WriteLine("-- High-Score Test (NEW scoring) --");
        double contTestThreshold = Math.Max(0.20, medContEntered);
        double revTestThreshold = Math.Max(0.15, medRevEntered);
        int newContEntered = entered.Count(c => c.PeakContScore >= contTestThreshold);
        int newContWins = entered.Count(c => c.PeakContScore >= contTestThreshold && c.IsWin);
        int newRevEntered = entered.Count(c => c.PeakRevScore >= revTestThreshold);
        int newRevWins = entered.Count(c => c.PeakRevScore >= revTestThreshold && c.IsWin);

        if (newContEntered > 0)
            Console.WriteLine($"  PeakCont >= {contTestThreshold:F2}: {newContWins}/{newContEntered} wins ({100.0*newContWins/Math.Max(1,newContEntered):F0}%)");
        else
            Console.WriteLine($"  PeakCont >= {contTestThreshold:F2}: 0 entered (threshold too high for this score range)");
        if (newRevEntered > 0)
            Console.WriteLine($"  PeakRev  >= {revTestThreshold:F2}: {newRevWins}/{newRevEntered} wins ({100.0*newRevWins/Math.Max(1,newRevEntered):F0}%)");
        else
            Console.WriteLine($"  PeakRev  >= {revTestThreshold:F2}: 0 entered (threshold too high for this score range)");

        // ── 18. Anti-predictive re-check ─────────────────────────
        bool stillAntiPredictive = (newContEntered >= 5 && newContWins == 0)
                                || (newRevEntered >= 5 && newRevWins == 0);
        Console.WriteLine();
        Console.WriteLine("── VERDICT ──");
        if (stillAntiPredictive)
            Console.WriteLine("  Event-driven scores are STILL anti-predictive. Scoring layer redesign needed.");
        else if (winners.Count < 5)
            Console.WriteLine("  Too few winners for definitive verdict. Recalibrate thresholds and re-run.");
        else
            Console.WriteLine("  Event-driven scores show potential. Recalibrate thresholds as recommended.");
        Console.WriteLine("============================================================");
    }

    // ── Threshold sweep helpers ───────────────────────────────────

    private static void PrintPercentileTable(string label, double[] vals)
    {
        if (vals.Length == 0) return;
        Array.Sort(vals);
        int n = vals.Length;
        Console.WriteLine($"{label}: min={vals[0]:F3} p10={vals[n/10]:F3} p25={vals[n/4]:F3} med={vals[n/2]:F3} p75={vals[3*n/4]:F3} p90={vals[9*n/10]:F3} max={vals[n-1]:F3}");
    }

    private static double Percentile(double[] vals, double pct)
    {
        if (vals.Length == 0) return 0;
        var sorted = vals.OrderBy(x => x).ToArray();
        int idx = (int)(pct * (sorted.Length - 1));
        return sorted[Math.Min(idx, sorted.Length - 1)];
    }

    private static void PrintActivation(string name, int count, int total)
    {
        Console.WriteLine($"  {name,-25}: {count,4}/{total} ({100.0 * count / Math.Max(1, total):F1}%)");
    }

    private static void PrintFamilyContribution(string label,
        List<CandidateRecord> winners, List<CandidateRecord> losers,
        Func<CandidateRecord, double> selector)
    {
        double wMean = winners.Average(selector);
        double lMean = losers.Average(selector);
        double diff = wMean - lMean;
        string verdict = diff > 0.02 ? "HELPS" : diff < -0.02 ? "HURTS" : "neutral";
        Console.WriteLine($"  {label,-12} {wMean,8:F3} {lMean,8:F3} {diff,8:+0.000;-0.000; 0.000} {verdict,10}");
    }

    // Approximate: check if exhaustion signals were present (revSignalCount includes absorption+reclaim too)
    private static bool _candidateHadExhaustion(CandidateRecord c)
        => c.RevSignalCount > 0; // conservative: includes all reversal signals

    // ── Private helpers ────────────────────────────────────────────

    private static void PrintScoreDistributions(string label,
        List<CandidateRecord> entered, List<CandidateRecord> noops,
        List<CandidateRecord> modeA, List<CandidateRecord> modeB,
        Func<CandidateRecord, double> selector)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {label} Distribution --");
        PrintGroupStat("All", _all(entered, noops), selector);
        PrintGroupStat("Entered", entered, selector);
        PrintGroupStat("Noop", noops, selector);
        PrintGroupStat("Mode A", modeA, selector);
        PrintGroupStat("Mode B", modeB, selector);
    }

    private static List<CandidateRecord> _all(List<CandidateRecord> a, List<CandidateRecord> b)
    {
        var all = new List<CandidateRecord>(a.Count + b.Count);
        all.AddRange(a); all.AddRange(b);
        return all;
    }

    private static void PrintGroupStat(string group, List<CandidateRecord> list,
        Func<CandidateRecord, double> selector)
    {
        if (list.Count == 0) { Console.WriteLine($"  {group,-10}: --"); return; }
        var vals = list.Select(selector).OrderBy(x => x).ToArray();
        double mean = vals.Average();
        double std = StdDev(vals, mean);
        double med = Median(vals);
        Console.WriteLine($"  {group,-10}: n={list.Count,3}  mean={mean:F3}  median={med:F3}  std={std:F3}  range=[{vals[0]:F3}, {vals[^1]:F3}]");
    }

    private static void PrintWinnerLoserStat(string label,
        List<CandidateRecord> winners, List<CandidateRecord> losers,
        Func<CandidateRecord, double> selector)
    {
        double wMean = winners.Average(selector);
        double lMean = losers.Average(selector);
        double diff = wMean - lMean;
        Console.WriteLine($"  {label,-15}: winners={wMean:F3}  losers={lMean:F3}  diff={diff:+0.000;-0.000; 0.000}");
    }

    private static void PrintStatLine(string label,
        List<CandidateRecord> modeA, List<CandidateRecord> modeB,
        Func<CandidateRecord, double> selector)
    {
        double aMean = modeA.Average(selector);
        double bMean = modeB.Average(selector);
        double d = aMean - bMean;
        Console.WriteLine($"  {label,-15}: mode_a={aMean:F3}  mode_b={bMean:F3}  diff={d:+0.000;-0.000; 0.000}");
    }

    private static double ComputePearson(double[] x, double[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        if (n < 2) return 0;
        double mx = x.Take(n).Average(), my = y.Take(n).Average();
        double num = 0, dx2 = 0, dy2 = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - mx, dy = y[i] - my;
            num += dx * dy;
            dx2 += dx * dx;
            dy2 += dy * dy;
        }
        double denom = Math.Sqrt(dx2 * dy2);
        return denom < 1e-12 ? 0 : num / denom;
    }

    private static double Median(double[] vals)
    {
        if (vals.Length == 0) return 0;
        var sorted = vals.OrderBy(x => x).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static double StdDev(double[] vals, double mean)
    {
        if (vals.Length < 2) return 0;
        double sumSq = vals.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (vals.Length - 1));
    }

    /// <summary>
    /// Simple separability score: how much do the mean score vectors of winners
    /// and losers differ, normalized? Score near 0 = random, > 0.05 = meaningful.
    /// Uses Euclidean distance between (cont_mean, rev_mean) centroids.
    /// </summary>
    private static double SeparabilityScore(
        List<CandidateRecord> winners, List<CandidateRecord> losers)
    {
        return SeparabilityScore(winners, losers,
            c => c.PeakContScore, c => c.PeakRevScore);
    }

    /// <summary>Layer-specific separability using custom score selectors.</summary>
    private static double SeparabilityScore(
        List<CandidateRecord> winners, List<CandidateRecord> losers,
        Func<CandidateRecord, double> contSel, Func<CandidateRecord, double> revSel)
    {
        if (winners.Count == 0 || losers.Count == 0) return -1;

        double wCont = winners.Average(contSel);
        double wRev = winners.Average(revSel);
        double lCont = losers.Average(contSel);
        double lRev = losers.Average(revSel);

        var allCont = winners.Select(contSel).Concat(losers.Select(contSel)).ToArray();
        var allRev = winners.Select(revSel).Concat(losers.Select(revSel)).ToArray();
        double poolContStd = StdDev(allCont, allCont.Average());
        double poolRevStd = StdDev(allRev, allRev.Average());

        double dCont = poolContStd > 0 ? (wCont - lCont) / poolContStd : 0;
        double dRev = poolRevStd > 0 ? (wRev - lRev) / poolRevStd : 0;
        return Math.Sqrt(dCont * dCont + dRev * dRev);
    }

    private static void PrintSepLine(string label, double score)
    {
        string verdict = score > 0.05 ? "SEPARABLE" : score > 0.01 ? "WEAK" : "NOISE";
        Console.WriteLine($"  {label,-25}: {score:F4} → {verdict}");
    }

    private static void PrintLayerComparison(string label,
        List<CandidateRecord> winners, List<CandidateRecord> losers,
        Func<CandidateRecord, double> selector)
    {
        double wMean = winners.Average(selector);
        double lMean = losers.Average(selector);
        double diff = wMean - lMean;
        string sep = Math.Abs(diff) > 0.03 ? "YES" : "no";
        Console.WriteLine($"  {label,-20} {wMean,8:F3} {lMean,8:F3} {diff,8:+0.000;-0.000; 0.000} {sep,6}");
    }

    /// <summary>Spearman rank correlation coefficient.</summary>
    private static double SpearmanRank(double[] x, double[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        if (n < 3) return 0;
        int[] rankX = Rank(x), rankY = Rank(y);
        return ComputePearson(rankX.Select(r => (double)r).ToArray(),
                              rankY.Select(r => (double)r).ToArray());
    }

    private static int[] Rank(double[] vals)
    {
        int n = vals.Length;
        var indexed = vals.Select((v, i) => (v, i)).OrderBy(p => p.v).ToArray();
        int[] ranks = new int[n];
        for (int i = 0; i < n; i++) ranks[indexed[i].i] = i + 1;
        return ranks;
    }

    // ── Record type ──────────────────────────────────────────────────

    private struct CandidateRecord
    {
        public int Id;
        public int CreatedTick;
        // ── Layer 1: Raw prototype scores (DTW, pre-aggregation) ──
        public double PeakContScore;
        public double PeakRevScore;
        // ── Layer 2: Aggregated scores (coherence: avg + persistence + conflict) ──
        public double AvgContScore;
        public double ContPersistence;
        public double AvgRevScore;
        public double RevPersistence;
        public double ConflictOverlap;
        // ── Layer 3: Effective scores (after aggregation + signal bonus) ──
        public double EffectiveContScore;
        public double EffectiveRevScore;
        // ── Layer 4: Family scores (market-structure, Phase C+) ──
        public double FamilyContScore;
        public double FamilyRevScore;
        public double FamilyBOSScore;
        public double FamilyManipScore;
        public double MetaScore;
        // ── Metadata ──
        public int RevSignalCount;
        public int ContSignalCount;
        public string FinalOutcome;  // "mode_a", "mode_b", "noop"
        public string NoopReason;
        public string TriggerSource; // "sweep", "bos", "consolidation", "pullback"
        public double PnL;
        public bool IsWin;
        public string ExitReason;
        public bool HasTradeOutcome;
    }
}
