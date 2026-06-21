using CEPx.Core;
using CEPx.Pipeline;

var fakeMarketEvent = new MarketEvent(0L, "BTCUSDT", 0.0, 0.0, 0.0, 0.0, 0L);
var cepEvent = PipelineFunctions.IngestTick(fakeMarketEvent) ?? new CepEvent(0L, "BTCUSDT", "SweepStart", 0.0, "");
var score = PipelineFunctions.ScoreEvent(cepEvent, new MarketEvent[0]);
var state = PipelineFunctions.WriteState(score);
var decision = PipelineFunctions.Decide(state);

Console.WriteLine($"CEPx: {decision.Action} on {decision.Symbol}");
