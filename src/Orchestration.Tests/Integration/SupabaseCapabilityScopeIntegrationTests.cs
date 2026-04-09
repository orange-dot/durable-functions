using FluentAssertions;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow;
using Orchestration.Core.Workflow.StateTypes;

namespace Orchestration.Tests.Integration;

[Collection(LocalSupabaseRuntimeCollection.Name)]
public sealed class SupabaseCapabilityScopeIntegrationTests(LocalSupabaseRuntimeFixture fixture)
{
    private readonly LocalSupabaseRuntimeFixture _fixture = fixture;

    [LocalSupabaseFact]
    public async Task ReadWriteTableGrant_supports_crud_against_live_supabase()
    {
        await _fixture.ResetAsync();

        var definition = CreateDefinition("capability-flow", "1.0.0");
        await _fixture.DefinitionStorage.SaveAsync(definition);

        var instanceId = $"instance-capability-{Guid.NewGuid():N}";
        await _fixture.RuntimeStore.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            InstanceId = instanceId,
            DefinitionId = LocalSupabaseRuntimeFixture.ComputeDefinitionId(definition.Id, definition.Version),
            DefinitionVersion = definition.Version,
            Status = WorkflowInstanceStatus.Waiting,
            CurrentStateName = "CaptureEvent",
            RuntimeState = new WorkflowRuntimeState
            {
                Input = new WorkflowInput
                {
                    WorkflowType = definition.Id,
                    Version = definition.Version,
                    EntityId = "order-123",
                    CorrelationId = "corr-capability",
                    Data = new Dictionary<string, object?>
                    {
                        ["source"] = "integration-test"
                    }
                },
                CurrentStep = "CaptureEvent",
                System = new SystemValues
                {
                    InstanceId = instanceId,
                    StartTime = DateTimeOffset.UtcNow,
                    CurrentTime = DateTimeOffset.UtcNow
                }
            }
        });

        var scope = _fixture.CapabilityFactory.CreateScope(
        [
            new CapabilityGrant("workflow-events", CapabilityKind.Table, CapabilityAccess.ReadWrite)
        ]);

        var table = scope.Table<CapabilityWorkflowEventRecord>("workflow-events");
        var recordId = Guid.NewGuid().ToString("N");

        var inserted = await table.InsertAsync(new CapabilityWorkflowEventRecord
        {
            EventId = recordId,
            InstanceId = instanceId,
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

    private static WorkflowDefinition CreateDefinition(string workflowType, string version)
    {
        return new WorkflowDefinition
        {
            Id = workflowType,
            Version = version,
            Name = $"{workflowType} workflow",
            Description = "Supabase capability integration test definition",
            StartAt = "CaptureEvent",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CaptureEvent"] = new SucceedStateDefinition()
            },
            Config = new WorkflowConfiguration
            {
                TimeoutSeconds = 300
            }
        };
    }
}
