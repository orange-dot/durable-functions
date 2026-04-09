using FluentAssertions;
using Orchestration.Core.Capabilities;

namespace Orchestration.Tests.Integration;

public sealed class SupabaseCapabilityScopeIntegrationTests(LocalSupabaseRuntimeFixture fixture)
    : IClassFixture<LocalSupabaseRuntimeFixture>
{
    private readonly LocalSupabaseRuntimeFixture _fixture = fixture;

    [LocalSupabaseFact]
    public async Task ReadWriteTableGrant_supports_crud_against_live_supabase()
    {
        await _fixture.ResetAsync();

        var scope = _fixture.CapabilityFactory.CreateScope(
        [
            new CapabilityGrant("workflow-events", CapabilityKind.Table, CapabilityAccess.ReadWrite)
        ]);

        var table = scope.Table<CapabilityWorkflowEventRecord>("workflow-events");
        var recordId = Guid.NewGuid().ToString("N");

        var inserted = await table.InsertAsync(new CapabilityWorkflowEventRecord
        {
            EventId = recordId,
            InstanceId = "instance-capability-test",
            EventName = "OrderPlaced",
            Payload = new Dictionary<string, object?>
            {
                ["orderId"] = "order-123"
            }
        });

        inserted.EventId.Should().Be(recordId);

        var loaded = await table.GetByIdAsync(recordId);
        loaded.Should().NotBeNull();
        loaded!.EventName.Should().Be("OrderPlaced");

        loaded.ConsumedByState = "Processed";
        await table.UpdateAsync(loaded);

        var updated = await table.GetByIdAsync(recordId);
        updated.Should().NotBeNull();
        updated!.ConsumedByState.Should().Be("Processed");

        await table.DeleteByIdAsync(recordId);

        var deleted = await table.GetByIdAsync(recordId);
        deleted.Should().BeNull();
    }
}
