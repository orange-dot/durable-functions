using Orchestration.Core.Capabilities;
using Orchestration.Core.Models;
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
