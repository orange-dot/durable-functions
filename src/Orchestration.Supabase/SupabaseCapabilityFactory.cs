using System.Reflection;
using Microsoft.Extensions.Options;
using Orchestration.Core.Capabilities;
using Orchestration.Supabase.Internal;
using Supabase.Postgrest.Models;

namespace Orchestration.Supabase;

public sealed class SupabaseCapabilityFactory
{
    private static readonly MethodInfo CreateGenericTableRegistrationMethod = typeof(SupabaseCapabilityFactory)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == nameof(CreateTableRegistration) && method.IsGenericMethodDefinition)
        ?? throw new InvalidOperationException("Could not locate table capability registration factory.");

    private readonly global::OrangeDot.Supabase.ISupabaseClient _client;
    private readonly SupabaseRuntimeOptions _options;

    public SupabaseCapabilityFactory(
        global::OrangeDot.Supabase.ISupabaseClient client,
        IOptions<SupabaseRuntimeOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public CapabilityScope CreateScope(IEnumerable<CapabilityGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var combinedGrants = CombineGrants(grants);
        var tables = new Dictionary<string, CapabilityScope.TableCapabilityRegistration>(StringComparer.OrdinalIgnoreCase);
        var recordTables = new Dictionary<string, CapabilityScope.RecordCapabilityRegistration>(StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, CapabilityScope.BucketCapabilityRegistration>(StringComparer.OrdinalIgnoreCase);
        var functions = new Dictionary<string, CapabilityScope.FunctionCapabilityRegistration>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in combinedGrants)
        {
            switch (grant.Kind)
            {
                case CapabilityKind.Table:
                    if (_options.TableBindings.ContainsKey(grant.ResourceName))
                    {
                        tables[grant.ResourceName] = CreateTableRegistration(grant.ResourceName, grant.Access);
                    }
                    else
                    {
                        recordTables[grant.ResourceName] = CreateRecordRegistration(grant.ResourceName, grant.Access);
                    }
                    break;

                case CapabilityKind.StorageBucket:
                    buckets[grant.ResourceName] = CreateBucketRegistration(grant.ResourceName, grant.Access);
                    break;

                case CapabilityKind.EdgeFunction:
                    functions[grant.ResourceName] = CreateFunctionRegistration(grant.ResourceName, grant.Access);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Capability kind '{grant.Kind}' is not supported by the Supabase capability factory.");
            }
        }

        return new CapabilityScope(tables, recordTables, buckets, functions);
    }

    private CapabilityScope.TableCapabilityRegistration CreateTableRegistration(string resourceName, CapabilityAccess access)
    {
        if (!_options.TableBindings.TryGetValue(resourceName, out var binding))
        {
            throw new InvalidOperationException($"Table capability '{resourceName}' is not mapped in Supabase runtime options.");
        }

        return (CapabilityScope.TableCapabilityRegistration)CreateGenericTableRegistrationMethod
            .MakeGenericMethod(binding.RecordType)
            .Invoke(this, [resourceName, access])!;
    }

    private CapabilityScope.BucketCapabilityRegistration CreateBucketRegistration(string resourceName, CapabilityAccess access)
    {
        if (!_options.StorageBucketBindings.TryGetValue(resourceName, out var binding))
        {
            throw new InvalidOperationException(
                $"Storage bucket capability '{resourceName}' is not mapped in Supabase runtime options.");
        }

        var bucket = _client.Storage.From(binding.BucketName);
        var adapter = new SupabaseArtifactBucket(resourceName, access, bucket);
        return new CapabilityScope.BucketCapabilityRegistration(access, adapter);
    }

    private CapabilityScope.FunctionCapabilityRegistration CreateFunctionRegistration(string resourceName, CapabilityAccess access)
    {
        if (!_options.EdgeFunctionBindings.TryGetValue(resourceName, out var binding))
        {
            throw new InvalidOperationException(
                $"Edge function capability '{resourceName}' is not mapped in Supabase runtime options.");
        }

        if (access == CapabilityAccess.Read)
        {
            throw new InvalidOperationException(
                $"Edge function capability '{resourceName}' cannot be granted read-only access.");
        }

        var adapter = new SupabaseEdgeFunctionInvoker(binding.FunctionName, _client.Functions);
        return new CapabilityScope.FunctionCapabilityRegistration(access, adapter);
    }

    private CapabilityScope.TableCapabilityRegistration CreateTableRegistration<TRecord>(string resourceName, CapabilityAccess access)
        where TRecord : BaseModel, new()
    {
        var adapter = new SupabaseTableCapability<TRecord>(_client);
        return new CapabilityScope.TableCapabilityRegistration(typeof(TRecord), access, adapter);
    }

    private CapabilityScope.RecordCapabilityRegistration CreateRecordRegistration(string resourceName, CapabilityAccess access)
    {
        if (!_options.RecordBindings.TryGetValue(resourceName, out var binding))
        {
            throw new InvalidOperationException(
                $"Table capability '{resourceName}' is not mapped in Supabase runtime options.");
        }

        var adapter = binding.Kind switch
        {
            SupabaseRecordCapabilityKind.Onboarding => new SupabaseOnboardingRecordCapability(_client),
            _ => throw new InvalidOperationException(
                $"Record capability '{resourceName}' uses unsupported binding kind '{binding.Kind}'.")
        };

        return new CapabilityScope.RecordCapabilityRegistration(access, adapter);
    }

    private static IReadOnlyList<CapabilityGrant> CombineGrants(IEnumerable<CapabilityGrant> grants)
    {
        var combined = new Dictionary<(CapabilityKind Kind, string ResourceName), CapabilityAccess>();

        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.ResourceName);

            var key = (grant.Kind, grant.ResourceName);

            if (combined.TryGetValue(key, out var existingAccess))
            {
                combined[key] = MergeAccess(existingAccess, grant.Access);
            }
            else
            {
                combined[key] = grant.Access;
            }
        }

        return combined
            .Select(entry => new CapabilityGrant(entry.Key.ResourceName, entry.Key.Kind, entry.Value))
            .ToArray();
    }

    private static CapabilityAccess MergeAccess(CapabilityAccess left, CapabilityAccess right)
    {
        if (left == right)
        {
            return left;
        }

        if (left == CapabilityAccess.ReadWrite || right == CapabilityAccess.ReadWrite)
        {
            return CapabilityAccess.ReadWrite;
        }

        return CapabilityAccess.ReadWrite;
    }
}
