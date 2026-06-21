using CEPx.Core;
using CEPx.Pipeline;

var fakeMarketEvent = default(MarketEvent);
var cepEvent = PipelineFunctions.IngestTick(fakeMarketEvent);
var score = PipelineFunctions.ScoreEvent(cepEvent.Value, new MarketEvent[0]);
var state = PipelineFunctions.WriteState(score);
var decision = PipelineFunctions.Decide(state);

Console.WriteLine($"CEPx: {decision.Action} on {decision.Symbol}");
