namespace CEPx.Core;

public readonly struct CepEvent
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly string Type;
    public readonly double Price;
    public readonly string Context;
}