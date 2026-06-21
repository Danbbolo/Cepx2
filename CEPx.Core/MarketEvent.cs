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
}