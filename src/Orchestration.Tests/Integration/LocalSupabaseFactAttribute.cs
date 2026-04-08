namespace Orchestration.Tests.Integration;

/// <summary>
/// Opt-in fact for tests that require a local Supabase stack.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalSupabaseFactAttribute : FactAttribute
{
    internal const string RunSupabaseTestsEnvironmentVariable = "ORCHESTRATION_RUN_SUPABASE_TESTS";

    public LocalSupabaseFactAttribute()
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable(RunSupabaseTestsEnvironmentVariable)))
        {
            Skip = $"Set {RunSupabaseTestsEnvironmentVariable}=1 to run local Supabase integration tests.";
        }
    }

    private static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "1", StringComparison.Ordinal))
        {
            return true;
        }

        return bool.TryParse(value, out var enabled) && enabled;
    }
}
