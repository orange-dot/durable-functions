using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestration.Core.Capabilities;

public enum CapabilityKind
{
    Table,
    StorageBucket,
    EdgeFunction
}

public enum CapabilityAccess
{
    Read,
    Write,
    ReadWrite
}

public sealed record CapabilityGrant(string ResourceName, CapabilityKind Kind, CapabilityAccess Access);

public interface IReadTable<TRecord>
    where TRecord : class
{
    Task<TRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}

public interface IWriteTable<TRecord>
    where TRecord : class
{
    Task<TRecord> InsertAsync(TRecord record, CancellationToken cancellationToken = default);

    Task<TRecord> UpdateAsync(TRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(TRecord record, CancellationToken cancellationToken = default);

    Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default);
}

public interface IReadWriteTable<TRecord> : IReadTable<TRecord>, IWriteTable<TRecord>
    where TRecord : class
{
}

public interface IReadRecordTable
{
    Task<Dictionary<string, object?>?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}

public interface IWriteRecordTable
{
    Task<Dictionary<string, object?>> InsertAsync(
        Dictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, object?>> UpdateAsync(
        Dictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default);

    Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default);
}

public interface IReadWriteRecordTable : IReadRecordTable, IWriteRecordTable
{
}

public interface IArtifactBucket
{
    Task<byte[]> DownloadAsync(string path, CancellationToken cancellationToken = default);

    Task<string> UploadAsync(
        byte[] data,
        string path,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    string GetPublicUrl(string path);
}

public interface IEdgeFunctionInvoker
{
    Task<string> InvokeAsync(
        Dictionary<string, object?>? body = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> InvokeAsync<TResponse>(
        Dictionary<string, object?>? body = null,
        CancellationToken cancellationToken = default)
        where TResponse : class;
}

public interface IActivityCapabilityScopeFactory
{
    CapabilityScope CreateScope(IEnumerable<CapabilityGrant> grants);
}

public sealed class CapabilityScope
{
    private readonly Dictionary<string, TableCapabilityRegistration> _tables;
    private readonly Dictionary<string, RecordCapabilityRegistration> _recordTables;
    private readonly Dictionary<string, BucketCapabilityRegistration> _buckets;
    private readonly Dictionary<string, FunctionCapabilityRegistration> _functions;

    internal CapabilityScope(
        IReadOnlyDictionary<string, TableCapabilityRegistration> tables,
        IReadOnlyDictionary<string, RecordCapabilityRegistration> recordTables,
        IReadOnlyDictionary<string, BucketCapabilityRegistration> buckets,
        IReadOnlyDictionary<string, FunctionCapabilityRegistration> functions)
    {
        _tables = new Dictionary<string, TableCapabilityRegistration>(tables, StringComparer.OrdinalIgnoreCase);
        _recordTables = new Dictionary<string, RecordCapabilityRegistration>(recordTables, StringComparer.OrdinalIgnoreCase);
        _buckets = new Dictionary<string, BucketCapabilityRegistration>(buckets, StringComparer.OrdinalIgnoreCase);
        _functions = new Dictionary<string, FunctionCapabilityRegistration>(functions, StringComparer.OrdinalIgnoreCase);
    }

    public IReadTable<TRecord> ReadTable<TRecord>(string resourceName)
        where TRecord : class
    {
        return ResolveTable<IReadTable<TRecord>>(resourceName, typeof(TRecord), CapabilityAccess.Read);
    }

    public IWriteTable<TRecord> WriteTable<TRecord>(string resourceName)
        where TRecord : class
    {
        return ResolveTable<IWriteTable<TRecord>>(resourceName, typeof(TRecord), CapabilityAccess.Write);
    }

    public IReadWriteTable<TRecord> Table<TRecord>(string resourceName)
        where TRecord : class
    {
        return ResolveTable<IReadWriteTable<TRecord>>(resourceName, typeof(TRecord), CapabilityAccess.ReadWrite);
    }

    public IReadRecordTable ReadRecordTable(string resourceName)
    {
        return ResolveRecordTable<IReadRecordTable>(resourceName, CapabilityAccess.Read);
    }

    public IWriteRecordTable WriteRecordTable(string resourceName)
    {
        return ResolveRecordTable<IWriteRecordTable>(resourceName, CapabilityAccess.Write);
    }

    public IReadWriteRecordTable RecordTable(string resourceName)
    {
        return ResolveRecordTable<IReadWriteRecordTable>(resourceName, CapabilityAccess.ReadWrite);
    }

    public IArtifactBucket Bucket(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        if (!_buckets.TryGetValue(resourceName, out var registration))
        {
            throw new InvalidOperationException($"Storage bucket capability '{resourceName}' is not available in this scope.");
        }

        return registration.Adapter;
    }

    public IEdgeFunctionInvoker Function(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        if (!_functions.TryGetValue(resourceName, out var registration))
        {
            throw new InvalidOperationException($"Edge function capability '{resourceName}' is not available in this scope.");
        }

        if (!AllowsAccess(registration.Access, CapabilityAccess.Write))
        {
            throw new InvalidOperationException(
                $"Edge function capability '{resourceName}' does not allow invoke access. Granted access: {registration.Access}.");
        }

        return registration.Adapter;
    }

    private TCapability ResolveTable<TCapability>(
        string resourceName,
        Type requestedRecordType,
        CapabilityAccess requiredAccess)
        where TCapability : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentNullException.ThrowIfNull(requestedRecordType);

        if (!_tables.TryGetValue(resourceName, out var registration))
        {
            throw new InvalidOperationException($"Table capability '{resourceName}' is not available in this scope.");
        }

        if (!AllowsAccess(registration.Access, requiredAccess))
        {
            throw new InvalidOperationException(
                $"Table capability '{resourceName}' does not allow {AccessLabel(requiredAccess)} access. Granted access: {registration.Access}.");
        }

        if (registration.RecordType != requestedRecordType)
        {
            throw new InvalidOperationException(
                $"Table capability '{resourceName}' is configured for record type '{registration.RecordType.FullName}' and cannot be resolved as '{requestedRecordType.FullName}'.");
        }

        if (registration.Adapter is not TCapability typedAdapter)
        {
            throw new InvalidOperationException(
                $"Table capability '{resourceName}' cannot satisfy {typeof(TCapability).Name}.");
        }

        return typedAdapter;
    }

    private TCapability ResolveRecordTable<TCapability>(
        string resourceName,
        CapabilityAccess requiredAccess)
        where TCapability : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        if (!_recordTables.TryGetValue(resourceName, out var registration))
        {
            throw new InvalidOperationException($"Record table capability '{resourceName}' is not available in this scope.");
        }

        if (!AllowsAccess(registration.Access, requiredAccess))
        {
            throw new InvalidOperationException(
                $"Record table capability '{resourceName}' does not allow {AccessLabel(requiredAccess)} access. Granted access: {registration.Access}.");
        }

        if (registration.Adapter is not TCapability typedAdapter)
        {
            throw new InvalidOperationException(
                $"Record table capability '{resourceName}' cannot satisfy {typeof(TCapability).Name}.");
        }

        return typedAdapter;
    }

    private static bool AllowsAccess(CapabilityAccess granted, CapabilityAccess required)
    {
        return granted switch
        {
            CapabilityAccess.ReadWrite => true,
            CapabilityAccess.Read when required == CapabilityAccess.Read => true,
            CapabilityAccess.Write when required == CapabilityAccess.Write => true,
            _ => false
        };
    }

    private static string AccessLabel(CapabilityAccess access)
    {
        return access switch
        {
            CapabilityAccess.Read => "read",
            CapabilityAccess.Write => "write",
            CapabilityAccess.ReadWrite => "read/write",
            _ => access.ToString()
        };
    }

    internal sealed record TableCapabilityRegistration(Type RecordType, CapabilityAccess Access, object Adapter);

    internal sealed record RecordCapabilityRegistration(CapabilityAccess Access, object Adapter);

    internal sealed record BucketCapabilityRegistration(CapabilityAccess Access, IArtifactBucket Adapter);

    internal sealed record FunctionCapabilityRegistration(CapabilityAccess Access, IEdgeFunctionInvoker Adapter);
}
