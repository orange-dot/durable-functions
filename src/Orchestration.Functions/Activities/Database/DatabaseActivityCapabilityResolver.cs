using Orchestration.Core.Capabilities;

namespace Orchestration.Functions.Activities.Database;

internal static class DatabaseActivityCapabilityResolver
{
    public static IReadRecordTable ResolveReadTable(
        IActivityCapabilityScopeFactory scopeFactory,
        string recordType,
        IReadOnlyList<CapabilityGrant>? capabilityGrants)
    {
        var effectiveGrants = GetEffectiveGrants(recordType, CapabilityAccess.Read, capabilityGrants);
        var tableGrant = GetSingleTableGrant(effectiveGrants, CapabilityAccess.Read);
        var scope = scopeFactory.CreateScope(effectiveGrants);
        return scope.ReadRecordTable(tableGrant.ResourceName);
    }

    public static IReadWriteRecordTable ResolveReadWriteTable(
        IActivityCapabilityScopeFactory scopeFactory,
        string recordType,
        IReadOnlyList<CapabilityGrant>? capabilityGrants)
    {
        var effectiveGrants = GetEffectiveGrants(recordType, CapabilityAccess.ReadWrite, capabilityGrants);
        var tableGrant = GetSingleTableGrant(effectiveGrants, CapabilityAccess.ReadWrite);
        var scope = scopeFactory.CreateScope(effectiveGrants);
        return scope.RecordTable(tableGrant.ResourceName);
    }

    private static IReadOnlyList<CapabilityGrant> GetEffectiveGrants(
        string recordType,
        CapabilityAccess requiredAccess,
        IReadOnlyList<CapabilityGrant>? capabilityGrants)
    {
        if (capabilityGrants is { Count: > 0 })
        {
            return capabilityGrants;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        return [new CapabilityGrant(recordType, CapabilityKind.Table, requiredAccess)];
    }

    private static CapabilityGrant GetSingleTableGrant(
        IReadOnlyList<CapabilityGrant> grants,
        CapabilityAccess requiredAccess)
    {
        var tableGrants = grants
            .Where(grant => grant.Kind == CapabilityKind.Table)
            .ToArray();

        if (tableGrants.Length != 1)
        {
            throw new InvalidOperationException(
                $"Database activities require exactly one table capability grant. Received {tableGrants.Length}.");
        }

        var grant = tableGrants[0];

        if (!AllowsAccess(grant.Access, requiredAccess))
        {
            throw new InvalidOperationException(
                $"Database activity requires {AccessLabel(requiredAccess)} access to '{grant.ResourceName}', but the grant only allows {AccessLabel(grant.Access)}.");
        }

        return grant;
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
}
