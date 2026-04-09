using Orchestration.Core.Capabilities;

namespace Orchestration.Supabase;

public sealed class SupabaseActivityCapabilityScopeFactory : IActivityCapabilityScopeFactory
{
    private readonly SupabaseCapabilityFactory _capabilityFactory;

    public SupabaseActivityCapabilityScopeFactory(SupabaseCapabilityFactory capabilityFactory)
    {
        _capabilityFactory = capabilityFactory;
    }

    public CapabilityScope CreateScope(IEnumerable<CapabilityGrant> grants)
    {
        return _capabilityFactory.CreateScope(grants);
    }
}
