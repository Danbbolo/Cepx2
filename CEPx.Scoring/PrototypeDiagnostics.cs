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
        string finalOutcome, string noopReason)
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
            PnL = 0,
            IsWin = false,
            ExitReason = "",
            HasTradeOutcome = false
        };
        _candidates.Add(rec);
        if (finalOutcome is "mode_a" or "mode_b")
            _enteredIndex = _candidates.Count - 1;
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
        Console.WriteLine($"  Aggregation delta:  {aggregationDelta:+.F4} → aggregation {aggVerdict} signal");
        Console.WriteLine($"  Full pipeline delta: {fullDelta:+.F4} → full pipeline {fullVerdict} signal");

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
        Console.WriteLine("=== FINAL VERDICT ===");
        Console.WriteLine("============================================================");

        // Q1: Are prototypes the main bottleneck?
        bool prototypesWeak = rawSep < 0.05;
        bool prototypesModerate = rawSep >= 0.05 && rawSep < 0.15;
        Console.WriteLine();
        Console.WriteLine("Q1: Are prototypes the main bottleneck?");
        if (prototypesWeak)
            Console.WriteLine("  YES — Raw DTW scores have no winner/loser separability.");
        else if (prototypesModerate)
            Console.WriteLine("  PARTIALLY — Raw DTW has weak separability but below useful threshold.");
        else
            Console.WriteLine("  NO — Raw prototypes have meaningful separability.");

        // Q2: Is scoring aggregation the main bottleneck?
        bool aggregationHelps = aggregationDelta > 0.02;
        bool aggregationHurts = aggregationDelta < -0.02;
        Console.WriteLine();
        Console.WriteLine("Q2: Is scoring aggregation the main bottleneck?");
        if (aggregationHelps)
            Console.WriteLine($"  NO — Aggregation IMPROVES separability by {aggregationDelta:+.F4}.");
        else if (aggregationHurts)
            Console.WriteLine($"  YES — Aggregation DEGRADES separability by {aggregationDelta:+.F4}.");
        else
            Console.WriteLine($"  NO — Aggregation is NEUTRAL (delta={aggregationDelta:+.F4}). The bottleneck is upstream.");

        // Q3: Are both weak?
        bool bothWeak = prototypesWeak && !aggregationHelps;
        Console.WriteLine();
        Console.WriteLine("Q3: Are both weak?");
        if (bothWeak)
            Console.WriteLine("  YES — Both prototypes AND aggregation are weak. The scoring pipeline has no discriminative power at any layer.");
        else if (prototypesWeak && aggregationHelps)
            Console.WriteLine("  PARTIALLY — Prototypes are weak but aggregation helps. The raw signal is noisy but aggregation extracts some structure.");
        else
            Console.WriteLine("  NO — At least one layer has meaningful signal.");

        // Q4: Does aggregation improve or destroy signal quality?
        Console.WriteLine();
        Console.WriteLine("Q4: Does aggregation improve or destroy signal quality?");
        if (aggregationDelta > 0.01)
            Console.WriteLine($"  IMPROVES (delta={aggregationDelta:+.F4}) — Coherence scoring adds value beyond raw peaks.");
        else if (aggregationDelta < -0.01)
            Console.WriteLine($"  DESTROYS (delta={aggregationDelta:+.F4}) — Aggregation is worse than using raw peaks alone.");
        else
            Console.WriteLine($"  NEUTRAL (delta={aggregationDelta:+.F4}) — Aggregation doesn't change signal quality meaningfully.");

        // Summary line
        Console.WriteLine();
        Console.WriteLine("── BOTTOM LINE ──");
        if (prototypesWeak && Math.Abs(aggregationDelta) < 0.02)
        {
            Console.WriteLine("  The prototype/scoring layer is the ROOT bottleneck. No amount of policy");
            Console.WriteLine("  architecture (coherence scoring, candidate lifecycle, signal boosting)");
            Console.WriteLine("  can compensate for DTW scores that don't discriminate good from bad.");
        }
        else if (aggregationHurts)
            Console.WriteLine("  Aggregation is ACTIVELY HURTING. Simplify to raw peaks or redesign aggregation.");
        else if (prototypesModerate && aggregationHelps)
            Console.WriteLine("  Both layers contribute. Prototypes are marginal; aggregation extracts value.");
        else
            Console.WriteLine("  Prototypes AND aggregation are weak. Full scoring redesign needed.");
        Console.WriteLine("============================================================");
    }

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
        Console.WriteLine($"  {label,-15}: winners={wMean:F3}  losers={lMean:F3}  diff={diff:+.F3}");
    }

    private static void PrintStatLine(string label,
        List<CandidateRecord> modeA, List<CandidateRecord> modeB,
        Func<CandidateRecord, double> selector)
    {
        double aMean = modeA.Average(selector);
        double bMean = modeB.Average(selector);
        Console.WriteLine($"  {label,-15}: mode_a={aMean:F3}  mode_b={bMean:F3}  diff={aMean - bMean:+.F3}");
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
        Console.WriteLine($"  {label,-20} {wMean,8:F3} {lMean,8:F3} {diff,8:+.F3} {sep,6}");
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
        // ── Metadata ──
        public int RevSignalCount;
        public int ContSignalCount;
        public string FinalOutcome;  // "mode_a", "mode_b", "noop"
        public string NoopReason;
        public double PnL;
        public bool IsWin;
        public string ExitReason;
        public bool HasTradeOutcome;
    }
}
