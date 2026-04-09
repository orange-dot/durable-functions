using Orchestration.Supabase.Internal;
using Supabase.Postgrest.Models;

namespace Orchestration.Supabase;

/// <summary>
/// Configuration for the Supabase-backed orchestration runtime substrate.
/// </summary>
public sealed class SupabaseRuntimeOptions
{
    private readonly Dictionary<string, SupabaseTableCapabilityBinding> _tableBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SupabaseRecordCapabilityBinding> _recordBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SupabaseStorageBucketCapabilityBinding> _storageBucketBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SupabaseEdgeFunctionCapabilityBinding> _edgeFunctionBindings = new(StringComparer.OrdinalIgnoreCase);

    public string? Url { get; set; }

    public string? ApiKey { get; set; }

    public SupabaseRuntimeOptions MapTable<TRecord>(string resourceName)
        where TRecord : BaseModel, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        AddUniqueBinding(
            _tableBindings,
            resourceName,
            new SupabaseTableCapabilityBinding(resourceName, typeof(TRecord)));
        return this;
    }

    public SupabaseRuntimeOptions MapOnboardingRecordTable(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        AddUniqueBinding(
            _recordBindings,
            resourceName,
            new SupabaseRecordCapabilityBinding(resourceName, SupabaseRecordCapabilityKind.Onboarding));
        return this;
    }

    public SupabaseRuntimeOptions MapStorageBucket(string resourceName, string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);

        AddUniqueBinding(
            _storageBucketBindings,
            resourceName,
            new SupabaseStorageBucketCapabilityBinding(resourceName, bucketName));
        return this;
    }

    public SupabaseRuntimeOptions MapEdgeFunction(string resourceName, string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        AddUniqueBinding(
            _edgeFunctionBindings,
            resourceName,
            new SupabaseEdgeFunctionCapabilityBinding(resourceName, functionName));
        return this;
    }

    internal IReadOnlyDictionary<string, SupabaseTableCapabilityBinding> TableBindings => _tableBindings;

    internal IReadOnlyDictionary<string, SupabaseRecordCapabilityBinding> RecordBindings => _recordBindings;

    internal IReadOnlyDictionary<string, SupabaseStorageBucketCapabilityBinding> StorageBucketBindings => _storageBucketBindings;

    internal IReadOnlyDictionary<string, SupabaseEdgeFunctionCapabilityBinding> EdgeFunctionBindings => _edgeFunctionBindings;

    private static void AddUniqueBinding<TBinding>(
        IDictionary<string, TBinding> bindings,
        string resourceName,
        TBinding binding)
    {
        if (bindings.ContainsKey(resourceName))
        {
            throw new InvalidOperationException($"Supabase capability '{resourceName}' is already mapped.");
        }

        bindings[resourceName] = binding;
    }
}
