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
        Console.WriteLine("-- Separability Verdict --");
        double sepScore = SeparabilityScore(winners, losers);
        if (sepScore > 0.05)
            Console.WriteLine($"  Cont/Rev can SEPARATE winners from losers (score={sepScore:F4} > 0.05)");
        else if (sepScore > 0.01)
            Console.WriteLine($"  Cont/Rev has WEAK separation (score={sepScore:F4}, near noise floor)");
        else
            Console.WriteLine($"  Cont/Rev CANNOT separate winners from losers (score={sepScore:F4} <= 0.01)");
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
        if (winners.Count == 0 || losers.Count == 0) return -1;

        double wCont = winners.Average(c => c.PeakContScore);
        double wRev = winners.Average(c => c.PeakRevScore);
        double lCont = losers.Average(c => c.PeakContScore);
        double lRev = losers.Average(c => c.PeakRevScore);

        // Pooled std for normalization
        var allCont = winners.Select(c => c.PeakContScore).Concat(losers.Select(c => c.PeakContScore)).ToArray();
        var allRev = winners.Select(c => c.PeakRevScore).Concat(losers.Select(c => c.PeakRevScore)).ToArray();
        double poolContStd = StdDev(allCont, allCont.Average());
        double poolRevStd = StdDev(allRev, allRev.Average());

        // Normalized Euclidean distance between centroids
        double dCont = poolContStd > 0 ? (wCont - lCont) / poolContStd : 0;
        double dRev = poolRevStd > 0 ? (wRev - lRev) / poolRevStd : 0;
        return Math.Sqrt(dCont * dCont + dRev * dRev);
    }

    // ── Record type ──────────────────────────────────────────────────

    private struct CandidateRecord
    {
        public int Id;
        public int CreatedTick;
        public double PeakContScore;
        public double AvgContScore;
        public double ContPersistence;
        public double PeakRevScore;
        public double AvgRevScore;
        public double RevPersistence;
        public double ConflictOverlap;
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
