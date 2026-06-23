"""
Fetch a CHD hourly parquet+zst file and convert to CSV for CEPx replay.
Usage: python tools/fetch_chd_sample.py [--date 2026-06-22] [--hour 20] [--symbol BTCUSDT] [--exchange binance_futures]
Output: tools/chd_sample.csv (timestamp_ms,price,volume,bid_size,ask_size)
"""
import argparse
import csv
import json
import os
import sys
import urllib.request

API_BASE = "https://api.cryptohftdata.com"
API_KEY = "bf90cc0213eb0d5d949343df0afef3a5741c2a758e91b0b0268a754223a32d86"
OUTPUT = os.path.join(os.path.dirname(__file__), "chd_sample.csv")

def get_jwt() -> str:
    """Exchange API key for short-lived JWT."""
    req = urllib.request.Request(
        f"{API_BASE}/jwt-token",
        method="POST",
        headers={"X-API-Key": API_KEY},
    )
    with urllib.request.urlopen(req) as resp:
        data = json.loads(resp.read())
    return data["jwt_token"]


def download_parquet(jwt: str, exchange: str, date: str, hour: str, symbol: str) -> bytes:
    """Download a parquet+zst file."""
    file_path = f"{exchange}/{date}/{int(hour):02d}/{symbol}_trades.parquet.zst"
    url = f"{API_BASE}/download?file={file_path}"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {jwt}"})
    print(f"Downloading: {url}")
    with urllib.request.urlopen(req) as resp:
        return resp.read()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--date", default="2026-06-22")
    parser.add_argument("--hour", default="20")
    parser.add_argument("--symbol", default="BTCUSDT")
    parser.add_argument("--exchange", default="binance_futures")
    args = parser.parse_args()

    print("Getting JWT token...")
    jwt = get_jwt()
    print(f"Token acquired (len={len(jwt)}).")

    print(f"Downloading {args.exchange}/{args.date}/{args.hour}/{args.symbol}_trades.parquet.zst ...")
    raw = download_parquet(jwt, args.exchange, args.date, args.hour, args.symbol)

    # Write raw parquet+zst to temp file for pandas to read
    tmp_parquet = os.path.join(os.path.dirname(__file__), "_tmp_trades.parquet.zst")
    with open(tmp_parquet, "wb") as f:
        f.write(raw)
    print(f"Downloaded {len(raw)} bytes.")

    # Parse with pandas (handles zstd + parquet natively)
    try:
        import pandas as pd
    except ImportError:
        print("ERROR: pandas not installed. Run: pip install pandas pyarrow zstandard")
        sys.exit(1)

    df = pd.read_parquet(tmp_parquet)
    print(f"Parsed {len(df)} rows. Columns: {list(df.columns)}")

    # Map to CEPx MarketEvent fields
    # Expected CHD columns: timestamp, price, size, side, ...
    # Adapt based on actual columns
    rows = []
    for _, row in df.iterrows():
        ts = int(row.get("timestamp", row.get("ts", 0)))
        if ts > 1e15:  # nanoseconds → milliseconds
            ts = ts // 1_000_000
        elif ts > 1e12:  # microseconds → milliseconds
            ts = ts // 1000

        price = float(row.get("price", 0))
        volume = float(row.get("size", row.get("qty", row.get("amount", 0))))
        bid_size = float(row.get("bid_size", 0))
        ask_size = float(row.get("ask_size", 0))

        rows.append([ts, price, volume, bid_size, ask_size])

    # Write CSV
    with open(OUTPUT, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["timestamp_ms", "price", "volume", "bid_size", "ask_size"])
        w.writerows(rows)

    print(f"Wrote {len(rows)} rows to {OUTPUT}")
    os.remove(tmp_parquet)
    print("Done.")


if __name__ == "__main__":
    main()
