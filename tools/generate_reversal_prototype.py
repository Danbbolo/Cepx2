"""
Offline generator for REVERSAL_PROTOTYPE in CEPx.Scoring.ScoringEngine.

Usage: python tools/generate_reversal_prototype.py [--days 30] [--horizon 20]

Matches the C# logic exactly:
  - Sweep detection: 5-tick window, range/avg >= 0.2% (DetectSweepStart)
  - Window extraction: 10-tick centered on sweep timestamp (ScoreEvent)
  - Normalization: min-max to [0,1] (ScoreEvent)
  - Labeling: forward return at horizon H vs sweep direction

Output: ready-to-paste C# double[] literal for REVERSAL_PROTOTYPE.
"""

import argparse
import json
import math
import random
import sys
import time
import urllib.request
from collections import defaultdict

# ── Config (matches C# PipelineFunctions + ScoringEngine) ──────────
SWEEP_THRESHOLD_PCT = 0.2   # DetectSweepStart
SWEEP_WINDOW_TICKS = 5      # DetectSweepStart
WINDOW_SIZE = 10             # ScoreEvent extraction
KMEANS_K = 3                 # number of clusters to try
KMEANS_ITERS = 50            # k-means iterations
RANDOM_SEED = 42

def fetch_klines(symbol: str, interval: str, limit: int,
                 start_ms: int = 0, end_ms: int = 0) -> list[dict]:
    """Fetch Binance 1m klines. Matches FetchBinanceHistorical."""
    url = f"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}"
    if start_ms:
        url += f"&startTime={start_ms}"
    if end_ms:
        url += f"&endTime={end_ms}"
    print(f"  Fetching: {url}")
    with urllib.request.urlopen(url) as resp:
        data = json.loads(resp.read())
    ticks = []
    for row in data:
        ticks.append({
            "ts": int(row[0]),
            "price": float(row[4]),   # close price
            "volume": float(row[5]),
        })
    return ticks


def detect_sweeps(ticks: list[dict]) -> list[dict]:
    """
    Port of PipelineFunctions.DetectSweepStart.
    Returns list of sweeps with index, price, direction, timestamp.
    """
    sweeps = []
    window = SWEEP_WINDOW_TICKS
    for i in range(window - 1, len(ticks)):
        recent = ticks[i - window + 1 : i + 1]
        prices = [t["price"] for t in recent]
        high = max(prices)
        low = min(prices)
        avg = sum(prices) / len(prices)
        if avg <= 0:
            continue
        if (high - low) / avg * 100 < SWEEP_THRESHOLD_PCT:
            continue
        direction = "bullish" if prices[-1] > prices[0] else "bearish"
        sweeps.append({
            "idx": i,                      # index of last tick in sweep
            "price": prices[-1],
            "direction": direction,
            "ts": recent[-1]["ts"],
        })
    return sweeps


def extract_window(ticks: list[dict], sweep_idx: int) -> list[float] | None:
    """
    Port of ScoringEngine.ScoreEvent window extraction.
    Finds tick closest to sweep event timestamp, extracts 5 before + 4 after.
    Returns 10 min-max-normalized prices, or None if range is zero.
    """
    start = max(0, sweep_idx - 9)
    window_ticks = ticks[start : sweep_idx + 1]
    if len(window_ticks) < 10:
        return None

    event_ts = ticks[sweep_idx]["ts"]
    # Find center index closest to event timestamp
    center_idx = start
    best_diff = float("inf")
    for j in range(start, sweep_idx + 1):
        diff = abs(ticks[j]["ts"] - event_ts)
        if diff < best_diff:
            best_diff = diff
            center_idx = j

    w0 = max(0, center_idx - 5)
    w1 = min(len(ticks) - 1, w0 + 9)
    w0 = max(0, w1 - 9)
    prices = [ticks[j]["price"] for j in range(w0, w1 + 1)]

    if len(prices) < 10:
        return None

    # Min-max normalization (matches ScoreEvent exactly)
    p_min = min(prices)
    p_max = max(prices)
    p_range = p_max - p_min
    if p_range == 0:
        return None

    return [(p - p_min) / p_range for p in prices]


def label_sample(ticks: list[dict], sweep: dict, horizon: int) -> bool:
    """
    Label a sweep as reversed (True) or continuation (False).
    Reversal: forward return at horizon H moves against sweep direction.
    Matches: bullish sweep reversed if price[H] < sweep price.
    """
    future_idx = sweep["idx"] + horizon
    if future_idx >= len(ticks):
        return None  # insufficient forward data
    future_price = ticks[future_idx]["price"]
    if sweep["direction"] == "bullish":
        return future_price < sweep["price"]
    else:
        return future_price > sweep["price"]


def kmeans(samples: list[list[float]], k: int, iters: int) -> tuple[list[float], float]:
    """Simple k-means clustering. Returns best centroid and mean cluster distance."""
    rng = random.Random(RANDOM_SEED)
    # Random initialization
    centroids = rng.sample(samples, k)
    centroids = [c[:] for c in centroids]

    for _ in range(iters):
        # Assign
        clusters = defaultdict(list)
        for s in samples:
            best_c = min(range(k), key=lambda c: sum((s[i] - centroids[c][i]) ** 2 for i in range(10)))
            clusters[best_c].append(s)

        # Update
        new_centroids = []
        for c_idx in range(k):
            if clusters[c_idx]:
                avg = [0.0] * 10
                for s in clusters[c_idx]:
                    for i in range(10):
                        avg[i] += s[i]
                n = len(clusters[c_idx])
                new_centroids.append([v / n for v in avg])
            else:
                new_centroids.append(centroids[c_idx][:])

        # Check convergence
        max_delta = max(
            sum(abs(new_centroids[i][j] - centroids[i][j]) for j in range(10))
            for i in range(k)
        )
        centroids = new_centroids
        if max_delta < 1e-6:
            break

    # Pick best centroid (largest cluster)
    best_c = max(range(k), key=lambda c: len(clusters.get(c, [])))
    cluster = clusters.get(best_c, [])
    centroid = centroids[best_c]

    # Mean distance
    if cluster:
        mean_dist = sum(
            math.sqrt(sum((s[i] - centroid[i]) ** 2 for i in range(10)))
            for s in cluster
        ) / len(cluster)
    else:
        mean_dist = 0.0

    return centroid, mean_dist, len(cluster)


def format_csharp_array(values: list[float]) -> str:
    """Format a list of floats as a C# double[] literal."""
    parts = ", ".join(f"{v:.4f}" for v in values)
    return f"{{ {parts} }}"


def main():
    parser = argparse.ArgumentParser(description="Generate REVERSAL_PROTOTYPE for CEPx")
    parser.add_argument("--days", type=int, default=30, help="Days of historical data to fetch")
    parser.add_argument("--horizon", type=int, default=20, help="Forward tick horizon for reversal labeling")
    args = parser.parse_args()

    horizon = args.horizon
    num_days = args.days

    print(f"=== CEPx Reversal Prototype Generator ===")
    print(f"Horizon: {horizon} ticks | Data: {num_days} days | K={KMEANS_K}")
    print()

    # ── Fetch data ──────────────────────────────────────────────────
    end_ms = int(time.time() * 1000)
    day_ms = 24 * 60 * 60 * 1000
    all_ticks = []

    print(f"Fetching {num_days} days of 1m BTCUSDT klines...")
    for d in range(num_days, 0, -1):
        day_end = end_ms - (d - 1) * day_ms
        day_start = day_end - day_ms
        try:
            batch = fetch_klines("BTCUSDT", "1m", 1000, start_ms=day_start, end_ms=day_end)
            all_ticks.extend(batch)
            print(f"  Day {-d}: {len(batch)} candles")
        except Exception as e:
            print(f"  Day {-d}: FAILED ({e})")
        time.sleep(0.2)  # rate limit

    if len(all_ticks) < 100:
        print("ERROR: Not enough data. Exiting.")
        sys.exit(1)

    # Deduplicate by timestamp
    seen = set()
    unique = []
    for t in all_ticks:
        if t["ts"] not in seen:
            seen.add(t["ts"])
            unique.append(t)
    unique.sort(key=lambda t: t["ts"])
    print(f"\nTotal: {len(unique)} unique candles (deduplicated)")
    print(f"Range: {unique[0]['ts']} -> {unique[-1]['ts']}")

    # ── Detect sweeps ───────────────────────────────────────────────
    print("\nDetecting sweeps...")
    sweeps = detect_sweeps(unique)
    print(f"Found {len(sweeps)} sweeps")

    # ── Extract windows + label ─────────────────────────────────────
    print(f"\nExtracting 10-tick windows + labeling (horizon={horizon})...")
    reversal_windows = []
    continuation_windows = []
    skipped_insufficient = 0
    skipped_range_zero = 0

    for sweep in sweeps:
        window = extract_window(unique, sweep["idx"])
        if window is None:
            skipped_range_zero += 1
            continue

        label = label_sample(unique, sweep, horizon)
        if label is None:
            skipped_insufficient += 1
            continue

        if label:
            reversal_windows.append(window)
        else:
            continuation_windows.append(window)

    print(f"  Reversal samples:     {len(reversal_windows)}")
    print(f"  Continuation samples: {len(continuation_windows)}")
    print(f"  Skipped (range=0):    {skipped_range_zero}")
    print(f"  Skipped (insufficient forward data): {skipped_insufficient}")

    if len(reversal_windows) < 10:
        print(f"\nERROR: Only {len(reversal_windows)} reversal samples. Need at least 10 for clustering.")
        print("Try increasing --days or reducing --horizon.")
        sys.exit(1)

    # ── Cluster reversal samples ────────────────────────────────────
    print(f"\nRunning k-means (k={KMEANS_K}) on {len(reversal_windows)} reversal samples...")
    centroid, mean_dist, cluster_size = kmeans(reversal_windows, KMEANS_K, KMEANS_ITERS)
    print(f"  Best cluster size: {cluster_size}/{len(reversal_windows)}")
    print(f"  Mean cluster distance: {mean_dist:.4f}")

    # ── Output ──────────────────────────────────────────────────────
    print(f"\n{'='*60}")
    print(f"GENERATED REVERSAL_PROTOTYPE")
    print(f"{'='*60}")
    print(f"")
    print(f"// Reversal prototype generated {time.strftime('%Y-%m-%d')}")
    print(f"// Source: {num_days} days of 1m BTCUSDT klines from Binance")
    print(f"// Method: k-means (k={KMEANS_K}) on {len(reversal_windows)} reversal-labeled samples")
    print(f"// Labeling rule: forward return at H={horizon} ticks against sweep direction")
    print(f"//   Bullish sweep reversed if price[H] < sweep price")
    print(f"//   Bearish sweep reversed if price[H] > sweep price")
    print(f"// Cluster: {cluster_size} samples, mean distance = {mean_dist:.4f}")
    print(f"//")
    print(f"// Paste this into CEPx.Scoring/ScoringEngine.cs replacing REVERSAL_PROTOTYPE:")
    print(f"")
    csharp = format_csharp_array(centroid)
    print(f"    private static readonly double[] REVERSAL_PROTOTYPE =")
    print(f"        {csharp};")
    print(f"")

    # ── Diagnostics ─────────────────────────────────────────────────
    print(f"{'='*60}")
    print(f"DIAGNOSTICS")
    print(f"{'='*60}")
    print(f"")

    # Compare with current 5-sample prototype
    current = [0.4434, 0.5098, 0.6322, 0.5679, 0.7094, 0.7853, 0.4302, 0.3838, 0.2818, 0.2207]
    print(f"Current (5-sample) prototype: {format_csharp_array(current)}")
    print(f"Generated ({len(reversal_windows)}-sample) prototype: {format_csharp_array(centroid)}")
    print(f"")

    # Calculate similarity between current and generated
    dist = math.sqrt(sum((current[i] - centroid[i]) ** 2 for i in range(10)))
    print(f"Euclidean distance between current and generated: {dist:.4f}")
    print(f"(Lower = more similar. Large gap suggests the 5-sample prototype was far from data.)")
    print(f"")

    # Distribution check
    print(f"Reversal sample distribution (first 3 values of each window):")
    for i in range(min(5, len(reversal_windows))):
        w = reversal_windows[i]
        print(f"  [{', '.join(f'{v:.3f}' for v in w[:5])} ...]")
    print(f"  ... ({len(reversal_windows) - 5} more)" if len(reversal_windows) > 5 else "")

    print(f"\nDone. Copy the prototype array above into ScoringEngine.cs.")


if __name__ == "__main__":
    main()
