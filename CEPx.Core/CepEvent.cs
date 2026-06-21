namespace CEPx.Core;

public readonly struct CepEvent
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly string Type;
    public readonly double Price;
    public readonly string Context;

    public CepEvent(long timestamp, string symbol, string type, double price, string context)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        Type = type;
        Price = price;
        Context = context;
    }
}