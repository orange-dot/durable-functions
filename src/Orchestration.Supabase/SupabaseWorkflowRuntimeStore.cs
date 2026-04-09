using Microsoft.Extensions.Logging;
using Orchestration.Core.Contracts;
using Orchestration.Core.Models;
using Orchestration.Supabase.Internal;
using Orchestration.Supabase.Models;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace Orchestration.Supabase;

/// <summary>
/// Supabase-backed workflow runtime store for non-replay orchestration hosts.
/// </summary>
public sealed class SupabaseWorkflowRuntimeStore : IWorkflowRuntimeStore
{
    private static readonly string[] RunnableStatuses =
    [
        WorkflowInstanceStatus.Pending.ToString(),
        WorkflowInstanceStatus.Running.ToString(),
        WorkflowInstanceStatus.Waiting.ToString(),
        WorkflowInstanceStatus.Compensating.ToString()
    ];

    private readonly global::OrangeDot.Supabase.ISupabaseStatelessClient _client;
    private readonly ILogger<SupabaseWorkflowRuntimeStore> _logger;

    public SupabaseWorkflowRuntimeStore(
        global::OrangeDot.Supabase.ISupabaseStatelessClient client,
        ILogger<SupabaseWorkflowRuntimeStore> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<WorkflowInstanceRecord?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var row = await _client.Postgrest.Table<WorkflowInstanceRow>()
            .Filter("instance_id", Operator.Equals, instanceId)
            .Single(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : MapInstanceRow(row);
    }

    public async Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var row = MapInstanceRecord(instance);

        await _client.Postgrest.Table<WorkflowInstanceRow>()
            .Insert(
                row,
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Created Supabase workflow instance {InstanceId} for definition {DefinitionId}.",
            instance.InstanceId,
            instance.DefinitionId);
    }

    public async Task UpdateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var row = MapInstanceRecord(instance);

        await _client.Postgrest.Table<WorkflowInstanceRow>()
            .Filter("instance_id", Operator.Equals, instance.InstanceId)
            .Update(
                row,
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowInstanceRecord>> ListRunnableInstancesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Postgrest.Table<WorkflowInstanceRow>()
            .Filter("status", Operator.In, RunnableStatuses.ToList())
            .Order("updated_at", Ordering.Ascending)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models
            .Where(row => row.LeaseExpiresAt is null || row.LeaseExpiresAt <= asOf || string.IsNullOrWhiteSpace(row.LeaseOwner))
            .Select(MapInstanceRow)
            .ToList();
    }

    public async Task<bool> TryAcquireLeaseAsync(
        string instanceId,
        WorkflowLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(lease);
        return await _client.Postgrest.Rpc<bool>(
            "try_acquire_workflow_lease",
            new
            {
                p_instance_id = instanceId,
                p_owner_id = lease.OwnerId,
                p_expires_at = lease.ExpiresAt
            }).ConfigureAwait(false);
    }

    public async Task RenewLeaseAsync(
        string instanceId,
        WorkflowLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(lease);
        var renewed = await _client.Postgrest.Rpc<bool>(
            "renew_workflow_lease",
            new
            {
                p_instance_id = instanceId,
                p_owner_id = lease.OwnerId,
                p_expires_at = lease.ExpiresAt
            }).ConfigureAwait(false);

        if (!renewed)
        {
            throw new InvalidOperationException(
                $"Failed to renew lease for workflow instance '{instanceId}' as owner '{lease.OwnerId}'.");
        }
    }

    public async Task ReleaseLeaseAsync(
        string instanceId,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await _client.Postgrest.Rpc<bool>(
            "release_workflow_lease",
            new
            {
                p_instance_id = instanceId,
                p_owner_id = ownerId
            }).ConfigureAwait(false);
    }

    public async Task AppendStepExecutionAsync(
        WorkflowStepExecutionRecord stepExecution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stepExecution);
        await _client.Postgrest.Table<WorkflowStepExecutionRow>()
            .Insert(
                MapStepExecutionRecord(stepExecution),
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateStepExecutionAsync(
        WorkflowStepExecutionRecord stepExecution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stepExecution);
        await _client.Postgrest.Table<WorkflowStepExecutionRow>()
            .Filter("step_execution_id", Operator.Equals, stepExecution.StepExecutionId)
            .Update(
                MapStepExecutionRecord(stepExecution),
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowStepExecutionRecord>> ListStepExecutionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var response = await _client.Postgrest.Table<WorkflowStepExecutionRow>()
            .Filter("instance_id", Operator.Equals, instanceId)
            .Order("created_at", Ordering.Ascending)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(MapStepExecutionRow).ToList();
    }

    public async Task AppendEventAsync(
        WorkflowEventRecord workflowEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);
        await _client.Postgrest.Table<WorkflowEventRow>()
            .Insert(
                MapEventRecord(workflowEvent),
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowEventRecord>> ListUnconsumedEventsAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var response = await _client.Postgrest.Table<WorkflowEventRow>()
            .Filter("instance_id", Operator.Equals, instanceId)
            .Filter<object?>("consumed_at", Operator.Is, null)
            .Order("recorded_at", Ordering.Ascending)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(MapEventRow).ToList();
    }

    public async Task MarkEventConsumedAsync(
        string instanceId,
        string eventId,
        string consumedByState,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumedByState);
        await _client.Postgrest.Table<WorkflowEventRow>()
            .Filter("instance_id", Operator.Equals, instanceId)
            .Filter("event_id", Operator.Equals, eventId)
            .Filter<object?>("consumed_at", Operator.Is, null)
            .Set(model => model.ConsumedByState!, consumedByState)
            .Set(model => model.ConsumedAt!, consumedAt)
            .Update(
                new QueryOptions { Returning = QueryOptions.ReturnType.Minimal },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static WorkflowInstanceRecord MapInstanceRow(WorkflowInstanceRow row)
    {
        return new WorkflowInstanceRecord
        {
            InstanceId = row.InstanceId ?? throw new InvalidOperationException("Workflow instance row is missing its primary key."),
            DefinitionId = row.DefinitionId,
            DefinitionVersion = row.DefinitionVersion,
            Status = Enum.Parse<WorkflowInstanceStatus>(row.Status, ignoreCase: true),
            CurrentStateName = row.CurrentStateName,
            RuntimeState = SupabaseJson.DeserializeRuntimeValue<WorkflowRuntimeState>(row.RuntimeState),
            Lease = string.IsNullOrWhiteSpace(row.LeaseOwner) || row.LeaseExpiresAt is null
                ? null
                : new WorkflowLease
                {
                    OwnerId = row.LeaseOwner,
                    ExpiresAt = row.LeaseExpiresAt.Value
                },
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            CompletedAt = row.CompletedAt
        };
    }

    private static WorkflowInstanceRow MapInstanceRecord(WorkflowInstanceRecord record)
    {
        var workflowType = record.RuntimeState.Input?.WorkflowType
            ?? throw new InvalidOperationException(
                $"Workflow instance '{record.InstanceId}' must carry WorkflowRuntimeState.Input.WorkflowType to persist to Supabase.");

        return new WorkflowInstanceRow
        {
            InstanceId = record.InstanceId,
            DefinitionId = record.DefinitionId,
            DefinitionWorkflowType = workflowType,
            DefinitionVersion = record.DefinitionVersion,
            Status = record.Status.ToString(),
            CurrentStateName = record.CurrentStateName,
            RuntimeState = SupabaseJson.SerializeRuntimeValue(record.RuntimeState),
            LeaseOwner = record.Lease?.OwnerId,
            LeaseExpiresAt = record.Lease?.ExpiresAt,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            CompletedAt = record.CompletedAt
        };
    }

    private static WorkflowStepExecutionRecord MapStepExecutionRow(WorkflowStepExecutionRow row)
    {
        return new WorkflowStepExecutionRecord
        {
            StepExecutionId = row.StepExecutionId ?? throw new InvalidOperationException("Step execution row is missing its primary key."),
            InstanceId = row.InstanceId,
            StateName = row.StateName,
            ActivityName = row.ActivityName,
            Attempt = row.Attempt,
            IsCompensation = row.IsCompensation,
            Status = Enum.Parse<StepExecutionStatus>(row.Status, ignoreCase: true),
            Decision = SupabaseJson.DeserializeRuntimeValue<Orchestration.Core.Workflow.Interpreter.WorkflowDecision>(row.DecisionJson),
            Outcome = row.OutcomeJson is null
                ? null
                : SupabaseJson.DeserializeRuntimeValue<Orchestration.Core.Workflow.Interpreter.WorkflowDecisionOutcome>(row.OutcomeJson),
            CreatedAt = row.CreatedAt,
            StartedAt = row.StartedAt,
            FinishedAt = row.FinishedAt,
            ErrorCode = row.ErrorCode,
            ErrorMessage = row.ErrorMessage
        };
    }

    private static WorkflowStepExecutionRow MapStepExecutionRecord(WorkflowStepExecutionRecord record)
    {
        return new WorkflowStepExecutionRow
        {
            StepExecutionId = record.StepExecutionId,
            InstanceId = record.InstanceId,
            StateName = record.StateName,
            ActivityName = record.ActivityName,
            Attempt = record.Attempt,
            IsCompensation = record.IsCompensation,
            Status = record.Status.ToString(),
            DecisionJson = SupabaseJson.SerializeRuntimeValue(record.Decision),
            OutcomeJson = record.Outcome is null ? null : SupabaseJson.SerializeRuntimeValue(record.Outcome),
            CreatedAt = record.CreatedAt,
            StartedAt = record.StartedAt,
            FinishedAt = record.FinishedAt,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage
        };
    }

    private static WorkflowEventRecord MapEventRow(WorkflowEventRow row)
    {
        return new WorkflowEventRecord
        {
            EventId = row.EventId ?? throw new InvalidOperationException("Workflow event row is missing its primary key."),
            InstanceId = row.InstanceId,
            EventName = row.EventName,
            Payload = row.Payload is null ? null : SupabaseJson.DeserializeRuntimeValue<object?>(row.Payload),
            RecordedAt = row.RecordedAt,
            ConsumedAt = row.ConsumedAt,
            ConsumedByState = row.ConsumedByState
        };
    }

    private static WorkflowEventRow MapEventRecord(WorkflowEventRecord record)
    {
        return new WorkflowEventRow
        {
            EventId = record.EventId,
            InstanceId = record.InstanceId,
            EventName = record.EventName,
            Payload = record.Payload is null ? null : SupabaseJson.SerializeRuntimeValue(record.Payload),
            RecordedAt = record.RecordedAt,
            ConsumedAt = record.ConsumedAt,
            ConsumedByState = record.ConsumedByState
        };
    }
}
