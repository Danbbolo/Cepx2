using CEPx.Core;

namespace CEPx.Pipeline;

public static partial class PipelineFunctions
{
    public static bool LiveMode;

    /// <summary>
    /// Binance API key (read-only). Set via environment variable BINANCE_API_KEY
    /// or directly in code before calling FetchLiquidations.
    /// Only needed for authenticated endpoints (forceOrders, account data).
    /// </summary>
    public static string? BinanceApiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");

    /// <summary>
    /// Binance API secret (for HMAC-SHA256 signing). Set via BINANCE_API_SECRET.
    /// Required alongside ApiKey for endpoints that need signed requests.
    /// </summary>
    public static string? BinanceApiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

    /// <summary>
    /// CHD (Crypto Historical Data) API key for futures data with L2 depth.
    /// Set via environment variable CHD_API_KEY or directly in code.
    /// Used for higher-quality historical replay input.
    /// </summary>
    public static string? ChdApiKey = Environment.GetEnvironmentVariable("CHD_API_KEY");

    public static PolicyDecision Decide(BlackboardState state)
    {
        return Policy.PolicyEngine.Decide(state);
    }
}
