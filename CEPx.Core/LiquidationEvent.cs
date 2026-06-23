namespace CEPx.Core;

/// <summary>Binance Futures liquidation order.</summary>
public readonly struct LiquidationEvent
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly double Price;
    public readonly double Quantity;
    public readonly string Side;   // "BUY" or "SELL" — SELL = long liquidation
    public readonly string Type;   // "LIMIT" or "MARKET"

    public LiquidationEvent(long timestamp, string symbol, double price, double quantity, string side, string type)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        Price = price;
        Quantity = quantity;
        Side = side;
        Type = type;
    }

    /// <summary>True if this is a long liquidation (trader was long, forced to sell).</summary>
    public bool IsLongLiquidation => Side == "SELL";

    /// <summary>True if this is a short liquidation (trader was short, forced to buy).</summary>
    public bool IsShortLiquidation => Side == "BUY";
}
