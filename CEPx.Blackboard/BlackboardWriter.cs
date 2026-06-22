using CEPx.Core;
using StackExchange.Redis;
using System.Text.Json;

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
        var json = JsonSerializer.Serialize(state);
        var key = $"cepx:blackboard:{state.Symbol}";
        _db.HashSet(key, "state", json);
        _db.KeyExpire(key, TimeSpan.FromSeconds(3600));
    }

    public static BlackboardState? Read(string symbol)
    {
        if (_db == null) { Connect(); _db = _redis!.GetDatabase(); }
        var json = _db.HashGet($"cepx:blackboard:{symbol}", "state");
        if (json.IsNull) return null;
        return JsonSerializer.Deserialize<BlackboardState>(json!.ToString());
    }
}
