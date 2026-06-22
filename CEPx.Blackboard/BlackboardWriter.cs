using CEPx.Core;
using StackExchange.Redis;

namespace CEPx.Blackboard;

public static class BlackboardWriter
{
    private const string DEFAULT_HOST = "localhost";
    private const int DEFAULT_PORT = 6379;

    private static ConnectionMultiplexer? _redis;
    private static IDatabase? _db;

    public static void Connect(string host = DEFAULT_HOST, int port = DEFAULT_PORT)
    {
        _redis = ConnectionMultiplexer.Connect($"{host}:{port}");
        _db = _redis.GetDatabase();
    }

    public static void Write(BlackboardState state)
    {
        if (_db == null) { Connect(); _db = _redis!.GetDatabase(); }
        var key = $"cepx:blackboard:{state.Symbol}";
        var entries = new HashEntry[]
        {
            new("Timestamp", state.Timestamp),
            new("Symbol", state.Symbol),
            new("SweepActive", state.SweepActive ? "1" : "0"),
            new("PatternFamily", state.PatternFamily),
            new("PatternSimilarity", state.PatternSimilarity.ToString()),
            new("KalmanVelocity", state.KalmanVelocity.ToString()),
            new("UncertaintyUpper", state.UncertaintyUpper.ToString()),
            new("UncertaintyLower", state.UncertaintyLower.ToString()),
            new("AnomalyScore", state.AnomalyScore.ToString()),
            new("Regime", state.Regime),
            new("RegimeConfidence", state.RegimeConfidence.ToString()),
            new("LastAction", state.LastAction),
        };
        _db.HashSet(key, entries);
        _db.KeyExpire(key, TimeSpan.FromSeconds(3600));
    }

    public static BlackboardState? Read(string symbol)
    {
        if (_db == null) { Connect(); _db = _redis!.GetDatabase(); }
        var key = $"cepx:blackboard:{symbol}";
        if (!_db.KeyExists(key)) return null;
        var entries = _db.HashGetAll(key).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        return new BlackboardState(
            long.Parse(entries["Timestamp"]),
            entries["Symbol"],
            entries["SweepActive"] == "1",
            entries["PatternFamily"],
            double.Parse(entries["PatternSimilarity"]),
            double.Parse(entries["KalmanVelocity"]),
            double.Parse(entries["UncertaintyUpper"]),
            double.Parse(entries["UncertaintyLower"]),
            double.Parse(entries["AnomalyScore"]),
            entries["Regime"],
            double.Parse(entries["RegimeConfidence"]),
            entries["LastAction"]
        );
    }
}
