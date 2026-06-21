namespace CEPx.Core;

public readonly struct PolicyDecision
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly string Action;
    public readonly string Side;
    public readonly string Reason;
    public readonly double Quantity;

    public PolicyDecision(long timestamp, string symbol, string action, string side, string reason, double quantity)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        Action = action;
        Side = side;
        Reason = reason;
        Quantity = quantity;
    }
}