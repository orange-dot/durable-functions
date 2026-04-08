using OrangeDot.Supabase;

namespace Orchestration.Supabase.Internal;

internal static class SupabaseClientFactory
{
    internal static ISupabaseClient CreateInitializedClient(SupabaseRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException("Supabase runtime URL is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("Supabase runtime API key is required.");
        }

        using var configuredClient = global::OrangeDot.Supabase.SupabaseClient.Configure(new SupabaseOptions
        {
            Url = options.Url,
            AnonKey = options.ApiKey
        });

        using var hydratedClient = configuredClient.LoadPersistedSessionAsync().GetAwaiter().GetResult();
        var client = hydratedClient.InitializeAsync().GetAwaiter().GetResult();
        var serviceRoleHeaders = CreateServiceRoleHeaders(options.ApiKey);

        client.Postgrest.GetHeaders = serviceRoleHeaders;
        client.Auth.GetHeaders = serviceRoleHeaders;

        return client;
    }

    private static Func<Dictionary<string, string>> CreateServiceRoleHeaders(string apiKey)
    {
        return () => new Dictionary<string, string>(2)
        {
            ["apikey"] = apiKey,
            ["Authorization"] = $"Bearer {apiKey}"
        };
    }
}
