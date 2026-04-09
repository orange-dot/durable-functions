using System.Collections.Concurrent;
using System.Reflection;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Orchestration.Supabase.Internal;

internal sealed class SupabaseTableModelMetadata
{
    private static readonly ConcurrentDictionary<Type, SupabaseTableModelMetadata> Cache = new();

    private SupabaseTableModelMetadata(string primaryKeyColumn)
    {
        PrimaryKeyColumn = primaryKeyColumn;
    }

    public string PrimaryKeyColumn { get; }

    public static SupabaseTableModelMetadata For<TRecord>()
        where TRecord : BaseModel, new()
    {
        return Cache.GetOrAdd(typeof(TRecord), Create);
    }

    private static SupabaseTableModelMetadata Create(Type recordType)
    {
        ArgumentNullException.ThrowIfNull(recordType);

        var primaryKeys = recordType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => new
            {
                Property = property,
                Attribute = property.GetCustomAttribute<PrimaryKeyAttribute>()
            })
            .Where(candidate => candidate.Attribute is not null)
            .ToArray();

        if (primaryKeys.Length == 0)
        {
            throw new InvalidOperationException(
                $"Supabase table record type '{recordType.FullName}' must declare a [PrimaryKey] property.");
        }

        if (primaryKeys.Length != 1)
        {
            throw new InvalidOperationException(
                $"Supabase table record type '{recordType.FullName}' must declare exactly one [PrimaryKey] property for capability-based access.");
        }

        return new SupabaseTableModelMetadata(primaryKeys[0].Attribute!.ColumnName);
    }
}
