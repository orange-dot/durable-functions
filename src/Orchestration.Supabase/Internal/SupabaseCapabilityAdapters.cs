using Orchestration.Core.Capabilities;
using Orchestration.Core.Models;
using Supabase.Postgrest.Attributes;
using Supabase.Functions;
using Supabase.Postgrest;
using Supabase.Postgrest.Models;
using Supabase.Storage;
using static Supabase.Postgrest.Constants;

namespace Orchestration.Supabase.Internal;

internal sealed class SupabaseTableCapability<TRecord> : IReadWriteTable<TRecord>
    where TRecord : BaseModel, new()
{
    private readonly global::OrangeDot.Supabase.ISupabaseClient _client;
    private readonly SupabaseTableModelMetadata _metadata;

    public SupabaseTableCapability(global::OrangeDot.Supabase.ISupabaseClient client)
    {
        _client = client;
        _metadata = SupabaseTableModelMetadata.For<TRecord>();
    }

    public async Task<TRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.Ready.ConfigureAwait(false);

        return await _client.Table<TRecord>()
            .Filter(_metadata.PrimaryKeyColumn, Operator.Equals, id)
            .Single(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TRecord> InsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _client.Ready.ConfigureAwait(false);

        var response = await _client.Table<TRecord>()
            .Insert(
                record,
                new QueryOptions { Returning = QueryOptions.ReturnType.Representation },
                cancellationToken)
            .ConfigureAwait(false);

        return response.Model ?? record;
    }

    public async Task<TRecord> UpdateAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _client.Ready.ConfigureAwait(false);

        var response = await _client.Table<TRecord>()
            .Update(
                record,
                new QueryOptions { Returning = QueryOptions.ReturnType.Representation },
                cancellationToken)
            .ConfigureAwait(false);

        return response.Model ?? record;
    }

    public async Task DeleteAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _client.Ready.ConfigureAwait(false);

        await _client.Table<TRecord>()
            .Delete(
                record,
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.Ready.ConfigureAwait(false);

        await _client.Table<TRecord>()
            .Filter(_metadata.PrimaryKeyColumn, Operator.Equals, id)
            .Delete(
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class SupabaseOnboardingRecordCapability : IReadWriteRecordTable
{
    private readonly global::OrangeDot.Supabase.ISupabaseClient _client;

    public SupabaseOnboardingRecordCapability(global::OrangeDot.Supabase.ISupabaseClient client)
    {
        _client = client;
    }

    public async Task<Dictionary<string, object?>?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.Ready.ConfigureAwait(false);

        var record = await _client.Table<SupabaseOnboardingRecordModel>()
            .Filter("id", Operator.Equals, id)
            .Single(cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToDictionary(record);
    }

    public async Task<Dictionary<string, object?>> InsertAsync(
        Dictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _client.Ready.ConfigureAwait(false);

        var normalized = WorkflowRuntimeValueNormalizer.NormalizeDictionary(record, "$.record");
        var model = FromDictionary(normalized);

        var response = await _client.Table<SupabaseOnboardingRecordModel>()
            .Insert(
                model,
                new QueryOptions { Returning = QueryOptions.ReturnType.Representation },
                cancellationToken)
            .ConfigureAwait(false);

        return ToDictionary(response.Model ?? model);
    }

    public async Task<Dictionary<string, object?>> UpdateAsync(
        Dictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _client.Ready.ConfigureAwait(false);

        var normalized = WorkflowRuntimeValueNormalizer.NormalizeDictionary(record, "$.record");
        var model = FromDictionary(normalized);

        var response = await _client.Table<SupabaseOnboardingRecordModel>()
            .Update(
                model,
                new QueryOptions { Returning = QueryOptions.ReturnType.Representation },
                cancellationToken)
            .ConfigureAwait(false);

        return ToDictionary(response.Model ?? model);
    }

    public Task DeleteAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!record.TryGetValue("id", out var idValue) || idValue is not string id || string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Record payload must contain a non-empty string 'id' to delete.");
        }

        return DeleteByIdAsync(id, cancellationToken);
    }

    public async Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _client.Ready.ConfigureAwait(false);

        await _client.Table<SupabaseOnboardingRecordModel>()
            .Filter("id", Operator.Equals, id)
            .Delete(
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static SupabaseOnboardingRecordModel FromDictionary(Dictionary<string, object?> record)
    {
        if (!record.TryGetValue("id", out var idValue) || idValue is not string id || string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Record payload must contain a non-empty string 'id'.");
        }

        return new SupabaseOnboardingRecordModel
        {
            Id = id,
            EntityId = record.TryGetValue("entityId", out var entityId) && entityId is string entityIdString && !string.IsNullOrWhiteSpace(entityIdString)
                ? entityIdString
                : "unknown",
            Status = record.TryGetValue("status", out var status) && status is string statusString && !string.IsNullOrWhiteSpace(statusString)
                ? statusString
                : "Created",
            IdempotencyKey = record.TryGetValue("idempotencyKey", out var idempotencyKey) ? idempotencyKey?.ToString() : null,
            DataJson = SupabaseJson.SerializeRuntimeValue(record),
            CreatedAt = record.TryGetValue("createdAt", out var createdAt) && createdAt is DateTimeOffset createdAtValue
                ? createdAtValue
                : DateTimeOffset.UtcNow,
            UpdatedAt = record.TryGetValue("updatedAt", out var updatedAt) && updatedAt is DateTimeOffset updatedAtValue
                ? updatedAtValue
                : null,
            WorkflowInstanceId = record.TryGetValue("workflowInstanceId", out var workflowInstanceId)
                ? workflowInstanceId?.ToString()
                : null
        };
    }

    private static Dictionary<string, object?> ToDictionary(SupabaseOnboardingRecordModel record)
    {
        var data = record.DataJson is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : SupabaseJson.DeserializeRuntimeValue<Dictionary<string, object?>>(record.DataJson)
              ?? new Dictionary<string, object?>(StringComparer.Ordinal);

        data["id"] = record.Id;
        data["entityId"] = record.EntityId;
        data["status"] = record.Status;
        data["idempotencyKey"] = record.IdempotencyKey;
        data["createdAt"] = record.CreatedAt;
        data["updatedAt"] = record.UpdatedAt;

        if (!string.IsNullOrWhiteSpace(record.WorkflowInstanceId))
        {
            data["workflowInstanceId"] = record.WorkflowInstanceId;
        }

        return WorkflowRuntimeValueNormalizer.NormalizeDictionary(data, "$.record");
    }
}

[Table("onboarding_records")]
internal sealed class SupabaseOnboardingRecordModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = "Created";

    [Column("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [Column("data_json")]
    public object? DataJson { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("workflow_instance_id")]
    public string? WorkflowInstanceId { get; set; }
}

internal sealed class SupabaseArtifactBucket : IArtifactBucket
{
    private readonly CapabilityAccess _access;
    private readonly string _resourceName;
    private readonly global::Supabase.Storage.Interfaces.IStorageFileApi<global::Supabase.Storage.FileObject> _bucket;

    public SupabaseArtifactBucket(
        string resourceName,
        CapabilityAccess access,
        global::Supabase.Storage.Interfaces.IStorageFileApi<global::Supabase.Storage.FileObject> bucket)
    {
        _resourceName = resourceName;
        _access = access;
        _bucket = bucket;
    }

    public async Task<byte[]> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReadAccess();

        return await _bucket.Download(path, transformOptions: null, onProgress: null).ConfigureAwait(false);
    }

    public async Task<string> UploadAsync(
        byte[] data,
        string path,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureWriteAccess();

        var options = contentType is null
            ? null
            : new global::Supabase.Storage.FileOptions { ContentType = contentType };

        return await _bucket.Upload(data, path, options, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWriteAccess();

        await _bucket.Remove(path).ConfigureAwait(false);
    }

    public string GetPublicUrl(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureReadAccess();
        return _bucket.GetPublicUrl(path);
    }

    private void EnsureReadAccess()
    {
        if (_access is CapabilityAccess.Read or CapabilityAccess.ReadWrite)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Storage bucket capability '{_resourceName}' does not allow read access. Granted access: {_access}.");
    }

    private void EnsureWriteAccess()
    {
        if (_access is CapabilityAccess.Write or CapabilityAccess.ReadWrite)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Storage bucket capability '{_resourceName}' does not allow write access. Granted access: {_access}.");
    }
}

internal sealed class SupabaseEdgeFunctionInvoker : IEdgeFunctionInvoker
{
    private readonly global::Supabase.Functions.Interfaces.IFunctionsClient _functionsClient;
    private readonly string _functionName;

    public SupabaseEdgeFunctionInvoker(
        string functionName,
        global::Supabase.Functions.Interfaces.IFunctionsClient functionsClient)
    {
        _functionName = functionName;
        _functionsClient = functionsClient;
    }

    public Task<string> InvokeAsync(
        Dictionary<string, object?>? body = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = CreateInvokeOptions(body);
        return _functionsClient.Invoke(_functionName, null, options);
    }

    public Task<TResponse?> InvokeAsync<TResponse>(
        Dictionary<string, object?>? body = null,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = CreateInvokeOptions(body);
        return _functionsClient.Invoke<TResponse>(_functionName, null, options);
    }

    private static global::Supabase.Functions.Client.InvokeFunctionOptions CreateInvokeOptions(Dictionary<string, object?>? body)
    {
        var normalized = WorkflowRuntimeValueNormalizer.NormalizeDictionary(body, "$.edgeFunction.body");
        var payload = new Dictionary<string, object>(normalized?.Count ?? 0);

        if (normalized is not null)
        {
            foreach (var (key, value) in normalized)
            {
                payload[key] = value!;
            }
        }

        return new global::Supabase.Functions.Client.InvokeFunctionOptions
        {
            Body = payload
        };
    }
}
