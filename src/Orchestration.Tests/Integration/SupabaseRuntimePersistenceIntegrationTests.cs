using FluentAssertions;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow;
using Orchestration.Core.Workflow.Interpreter;
using Orchestration.Core.Workflow.StateTypes;

namespace Orchestration.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SupabaseRuntimePersistenceIntegrationTests(LocalSupabaseRuntimeFixture fixture)
    : IClassFixture<LocalSupabaseRuntimeFixture>
{
    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task WorkflowDefinitions_Save_Get_List_And_Delete_Promotes_Previous_Latest()
    {
        await fixture.ResetAsync();

        var v1 = CreateDefinition("order-processing", "1.0.0");
        var v2 = CreateDefinition("order-processing", "2.0.0");

        await fixture.DefinitionStorage.SaveAsync(v1);
        await fixture.DefinitionStorage.SaveAsync(v2);

        var latest = await fixture.DefinitionStorage.GetAsync("order-processing");
        var specific = await fixture.DefinitionStorage.GetAsync("order-processing", "1.0.0");
        var versions = await fixture.DefinitionStorage.ListVersionsAsync("order-processing");
        var types = await fixture.DefinitionStorage.ListWorkflowTypesAsync();

        latest.Version.Should().Be("2.0.0");
        specific.Version.Should().Be("1.0.0");
        versions.Should().Contain(["1.0.0", "2.0.0"]);
        types.Should().Contain("order-processing");

        await fixture.DefinitionStorage.DeleteAsync("order-processing", "2.0.0");

        var promoted = await fixture.DefinitionStorage.GetAsync("order-processing");
        promoted.Version.Should().Be("1.0.0");
    }

    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task WorkflowInstances_Create_Get_Update_And_ListRunnable_RoundTrip_State()
    {
        await fixture.ResetAsync();

        var definition = CreateDefinition("payment-flow", "1.0.0");
        await fixture.DefinitionStorage.SaveAsync(definition);

        var instance = CreateInstanceRecord(
            definition,
            instanceId: $"wf-{Guid.NewGuid():N}",
            status: WorkflowInstanceStatus.Pending,
            lease: null);

        await fixture.RuntimeStore.CreateInstanceAsync(instance);

        var created = await fixture.RuntimeStore.GetInstanceAsync(instance.InstanceId);
        created.Should().NotBeNull();
        created!.DefinitionId.Should().Be(LocalSupabaseRuntimeFixture.ComputeDefinitionId(definition.Id, definition.Version));
        created.RuntimeState.CurrentStep.Should().Be("ChargeCard");
        created.RuntimeState.Variables["order"].Should().BeOfType<Dictionary<string, object?>>();
        created.RuntimeState.StepResults["previous"].Should().BeOfType<List<object?>>();
        AssertContainsNoJsonElements(created.RuntimeState);

        var runnable = await fixture.RuntimeStore.ListRunnableInstancesAsync(DateTimeOffset.UtcNow.AddMinutes(5));
        runnable.Should().ContainSingle(model => model.InstanceId == instance.InstanceId);

        var updated = new WorkflowInstanceRecord
        {
            InstanceId = created.InstanceId,
            DefinitionId = created.DefinitionId,
            DefinitionVersion = created.DefinitionVersion,
            Status = WorkflowInstanceStatus.Waiting,
            CurrentStateName = "AwaitApproval",
            RuntimeState = created.RuntimeState,
            Lease = new WorkflowLease
            {
                OwnerId = "runner-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            },
            CreatedAt = created.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = null
        };
        updated.RuntimeState.CurrentStep = "AwaitApproval";
        updated.RuntimeState.PendingDecision = new WaitForEventDecision("AwaitApproval", "approval.received");

        await fixture.RuntimeStore.UpdateInstanceAsync(updated);

        var reloaded = await fixture.RuntimeStore.GetInstanceAsync(instance.InstanceId);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(WorkflowInstanceStatus.Waiting);
        reloaded.CurrentStateName.Should().Be("AwaitApproval");
        reloaded.Lease.Should().NotBeNull();
        reloaded.Lease!.OwnerId.Should().Be("runner-1");
        reloaded.RuntimeState.PendingDecision.Should().BeOfType<WaitForEventDecision>();
        AssertContainsNoJsonElements(reloaded.RuntimeState);

        var excludedWhileLeased = await fixture.RuntimeStore.ListRunnableInstancesAsync(DateTimeOffset.UtcNow);
        excludedWhileLeased.Should().NotContain(model => model.InstanceId == instance.InstanceId);

        reloaded.Lease = new WorkflowLease
        {
            OwnerId = "runner-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        reloaded.UpdatedAt = DateTimeOffset.UtcNow;

        await fixture.RuntimeStore.UpdateInstanceAsync(reloaded);

        var includedWhenExpired = await fixture.RuntimeStore.ListRunnableInstancesAsync(DateTimeOffset.UtcNow);
        includedWhenExpired.Should().Contain(model => model.InstanceId == instance.InstanceId);
    }

    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task WorkflowLeases_Acquire_Renew_And_Release_Are_Atomic()
    {
        await fixture.ResetAsync();

        var definition = CreateDefinition("lease-flow", "1.0.0");
        await fixture.DefinitionStorage.SaveAsync(definition);

        var instance = CreateInstanceRecord(
            definition,
            instanceId: $"wf-{Guid.NewGuid():N}",
            status: WorkflowInstanceStatus.Running,
            lease: null);

        await fixture.RuntimeStore.CreateInstanceAsync(instance);

        var firstLease = new WorkflowLease
        {
            OwnerId = "runner-a",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var secondLease = new WorkflowLease
        {
            OwnerId = "runner-b",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        (await fixture.RuntimeStore.TryAcquireLeaseAsync(instance.InstanceId, firstLease)).Should().BeTrue();
        (await fixture.RuntimeStore.TryAcquireLeaseAsync(instance.InstanceId, secondLease)).Should().BeFalse();

        firstLease = new WorkflowLease
        {
            OwnerId = firstLease.OwnerId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        await fixture.RuntimeStore.RenewLeaseAsync(instance.InstanceId, firstLease);
        await fixture.RuntimeStore.ReleaseLeaseAsync(instance.InstanceId, firstLease.OwnerId);

        (await fixture.RuntimeStore.TryAcquireLeaseAsync(instance.InstanceId, secondLease)).Should().BeTrue();
    }

    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task StepExecutions_Append_Update_And_List_RoundTrip_Decision_And_Outcome()
    {
        await fixture.ResetAsync();

        var definition = CreateDefinition("steps-flow", "1.0.0");
        await fixture.DefinitionStorage.SaveAsync(definition);

        var instance = CreateInstanceRecord(
            definition,
            instanceId: $"wf-{Guid.NewGuid():N}",
            status: WorkflowInstanceStatus.Running,
            lease: null);

        await fixture.RuntimeStore.CreateInstanceAsync(instance);

        var pending = new WorkflowStepExecutionRecord
        {
            StepExecutionId = Guid.NewGuid().ToString("N"),
            InstanceId = instance.InstanceId,
            StateName = "ChargeCard",
            ActivityName = "ChargePayment",
            Attempt = 1,
            IsCompensation = false,
            Status = StepExecutionStatus.Pending,
            Decision = new ExecuteActivityDecision(
                "ChargeCard",
                "ChargePayment",
                new Dictionary<string, object?>
                {
                    ["amount"] = 125.50m,
                    ["currency"] = "EUR",
                    ["items"] = new List<object?> { "sku-1", 2 }
                }),
            StartedAt = DateTimeOffset.UtcNow
        };

        await fixture.RuntimeStore.AppendStepExecutionAsync(pending);

        pending.Status = StepExecutionStatus.Completed;
        pending.Outcome = new ActivityCompletedOutcome(new Dictionary<string, object?>
        {
            ["transactionId"] = "txn_123",
            ["settled"] = true
        });
        pending.FinishedAt = DateTimeOffset.UtcNow;

        await fixture.RuntimeStore.UpdateStepExecutionAsync(pending);

        var executions = await fixture.RuntimeStore.ListStepExecutionsAsync(instance.InstanceId);
        executions.Should().ContainSingle();
        var reloaded = executions.Single();
        reloaded.Status.Should().Be(StepExecutionStatus.Completed);
        reloaded.Decision.Should().BeOfType<ExecuteActivityDecision>();
        reloaded.Outcome.Should().BeOfType<ActivityCompletedOutcome>();
        var decision = (ExecuteActivityDecision)reloaded.Decision;
        decision.Input.Should().BeOfType<Dictionary<string, object?>>();
        var outcome = (ActivityCompletedOutcome)reloaded.Outcome!;
        outcome.Output.Should().BeOfType<Dictionary<string, object?>>();
        AssertContainsNoJsonElements(reloaded.Decision);
        AssertContainsNoJsonElements(reloaded.Outcome!);
    }

    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task WorkflowEvents_Append_List_And_Consume_RoundTrip_Payload()
    {
        await fixture.ResetAsync();

        var definition = CreateDefinition("event-flow", "1.0.0");
        await fixture.DefinitionStorage.SaveAsync(definition);

        var instance = CreateInstanceRecord(
            definition,
            instanceId: $"wf-{Guid.NewGuid():N}",
            status: WorkflowInstanceStatus.Waiting,
            lease: null);

        await fixture.RuntimeStore.CreateInstanceAsync(instance);

        var first = new WorkflowEventRecord
        {
            EventId = Guid.NewGuid().ToString("N"),
            InstanceId = instance.InstanceId,
            EventName = "approval.received",
            Payload = new Dictionary<string, object?>
            {
                ["approved"] = true,
                ["reviewer"] = new Dictionary<string, object?>
                {
                    ["id"] = "user-1",
                    ["name"] = "Approver"
                }
            }
        };
        var second = new WorkflowEventRecord
        {
            EventId = Guid.NewGuid().ToString("N"),
            InstanceId = instance.InstanceId,
            EventName = "note.added",
            Payload = new Dictionary<string, object?>
            {
                ["text"] = "looks good",
                ["attachments"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["name"] = "invoice.pdf" }
                }
            }
        };

        await fixture.RuntimeStore.AppendEventAsync(first);
        await fixture.RuntimeStore.AppendEventAsync(second);

        var unconsumed = await fixture.RuntimeStore.ListUnconsumedEventsAsync(instance.InstanceId);
        unconsumed.Should().HaveCount(2);
        unconsumed.Select(model => model.EventId).Should().ContainInOrder(first.EventId, second.EventId);
        AssertContainsNoJsonElements(unconsumed[0].Payload);
        AssertContainsNoJsonElements(unconsumed[1].Payload);

        await fixture.RuntimeStore.MarkEventConsumedAsync(
            instance.InstanceId,
            first.EventId,
            consumedByState: "AwaitApproval",
            consumedAt: DateTimeOffset.UtcNow);

        var remaining = await fixture.RuntimeStore.ListUnconsumedEventsAsync(instance.InstanceId);
        remaining.Should().ContainSingle(model => model.EventId == second.EventId);
    }

    private static WorkflowDefinition CreateDefinition(string workflowType, string version)
    {
        return new WorkflowDefinition
        {
            Id = workflowType,
            Version = version,
            Name = $"{workflowType} workflow",
            Description = "Supabase runtime persistence integration test definition",
            StartAt = "ChargeCard",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["ChargeCard"] = new TaskStateDefinition
                {
                    Activity = "ChargePayment",
                    ResultPath = "$.stepResults.charge",
                    Next = "Done",
                    Input = new Dictionary<string, object?>
                    {
                        ["amount"] = "$.variables.order.total"
                    },
                    CompensateWith = "RefundPayment"
                },
                ["Done"] = new SucceedStateDefinition()
            },
            Config = new WorkflowConfiguration
            {
                TimeoutSeconds = 300
            }
        };
    }

    private static WorkflowInstanceRecord CreateInstanceRecord(
        WorkflowDefinition definition,
        string instanceId,
        WorkflowInstanceStatus status,
        WorkflowLease? lease)
    {
        return new WorkflowInstanceRecord
        {
            InstanceId = instanceId,
            DefinitionId = LocalSupabaseRuntimeFixture.ComputeDefinitionId(definition.Id, definition.Version),
            DefinitionVersion = definition.Version,
            Status = status,
            CurrentStateName = "ChargeCard",
            RuntimeState = new WorkflowRuntimeState
            {
                Input = new WorkflowInput
                {
                    WorkflowType = definition.Id,
                    Version = definition.Version,
                    EntityId = "order-123",
                    CorrelationId = "corr-123",
                    Data = new Dictionary<string, object?>
                    {
                        ["customerId"] = "cust-1",
                        ["priority"] = 2
                    }
                },
                Variables = new Dictionary<string, object?>
                {
                    ["order"] = new Dictionary<string, object?>
                    {
                        ["id"] = "order-123",
                        ["total"] = 125.50m,
                        ["lines"] = new List<object?>
                        {
                            new Dictionary<string, object?> { ["sku"] = "sku-1", ["qty"] = 2 }
                        }
                    }
                },
                StepResults = new Dictionary<string, object?>
                {
                    ["previous"] = new List<object?> { 1, "two", true }
                },
                ExecutedSteps =
                [
                    new ExecutedStep
                    {
                        StepName = "ValidateOrder",
                        StepType = "Task",
                        ActivityName = "ValidateOrder",
                        CompensationActivity = "UndoValidation",
                        Input = new Dictionary<string, object?> { ["orderId"] = "order-123" },
                        Output = new Dictionary<string, object?> { ["ok"] = true }
                    }
                ],
                CurrentStep = "ChargeCard",
                PendingDecision = new ExecuteActivityDecision(
                    "ChargeCard",
                    "ChargePayment",
                    new Dictionary<string, object?> { ["amount"] = 125.50m }),
                System = new SystemValues
                {
                    InstanceId = instanceId,
                    StartTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CurrentTime = DateTimeOffset.UtcNow,
                    RetryCount = 0
                }
            },
            Lease = lease,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void AssertContainsNoJsonElements(object? value)
    {
        value.Should().NotBeOfType<System.Text.Json.JsonElement>();

        switch (value)
        {
            case IDictionary<string, object?> dictionary:
                foreach (var child in dictionary.Values)
                {
                    AssertContainsNoJsonElements(child);
                }
                break;
            case IEnumerable<object?> sequence:
                foreach (var child in sequence)
                {
                    AssertContainsNoJsonElements(child);
                }
                break;
            case WorkflowRuntimeState state:
                AssertContainsNoJsonElements(state.Variables);
                AssertContainsNoJsonElements(state.StepResults);
                AssertContainsNoJsonElements(state.PendingDecision);
                foreach (var step in state.ExecutedSteps)
                {
                    AssertContainsNoJsonElements(step.Input);
                    AssertContainsNoJsonElements(step.Output);
                }
                break;
            case ExecuteActivityDecision executeActivityDecision:
                AssertContainsNoJsonElements(executeActivityDecision.Input);
                break;
            case CompleteWorkflowDecision completeWorkflowDecision:
                AssertContainsNoJsonElements(completeWorkflowDecision.Output);
                break;
            case ActivityCompletedOutcome completedOutcome:
                AssertContainsNoJsonElements(completedOutcome.Output);
                break;
            case EventReceivedOutcome eventReceivedOutcome:
                AssertContainsNoJsonElements(eventReceivedOutcome.Payload);
                break;
        }
    }
}
