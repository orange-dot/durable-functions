using Orchestration.Core.Capabilities;

namespace Orchestration.Functions.Capabilities;

internal sealed class UnavailableActivityCapabilityScopeFactory : IActivityCapabilityScopeFactory
{
    public CapabilityScope CreateScope(IEnumerable<CapabilityGrant> grants)
    {
        throw new InvalidOperationException(
            "Activity capability scopes are not configured. Register Supabase orchestration persistence before invoking capability-backed database activities.");
    }
}
