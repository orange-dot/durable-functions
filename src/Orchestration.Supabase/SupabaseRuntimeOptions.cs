namespace Orchestration.Supabase;

/// <summary>
/// Configuration for the Supabase-backed orchestration runtime substrate.
/// </summary>
public sealed class SupabaseRuntimeOptions
{
    public string? Url { get; set; }

    public string? ApiKey { get; set; }
}
