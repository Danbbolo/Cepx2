namespace CEPx.Core;

public readonly struct MarketEvent
{
    public readonly long Timestamp;
    public readonly string Symbol;
    public readonly double Price;
    public readonly double Volume;
    public readonly double BidSize;
    public readonly double AskSize;
    public readonly long SequenceId;

    public MarketEvent(long timestamp, string symbol, double price, double volume, double bidSize, double askSize, long sequenceId)
    {
        Timestamp = timestamp;
        Symbol = symbol;
        Price = price;
        Volume = volume;
        BidSize = bidSize;
        AskSize = askSize;
        SequenceId = sequenceId;
    }
}