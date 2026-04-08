using Orchestration.Core.Models;

namespace Orchestration.Core.Contracts;

/// <summary>
/// Durable persistence boundary for workflow runtime state in non-replay hosts.
/// </summary>
public interface IWorkflowRuntimeStore
{
    /// <summary>
    /// Loads a persisted workflow instance snapshot.
    /// </summary>
    Task<WorkflowInstanceRecord?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new workflow instance snapshot.
    /// </summary>
    Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the latest workflow instance snapshot.
    /// </summary>
    Task UpdateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow instances that should be considered runnable as of the supplied time.
    /// </summary>
    Task<IReadOnlyList<WorkflowInstanceRecord>> ListRunnableInstancesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to atomically claim a lease for a workflow instance.
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(
        string instanceId,
        WorkflowLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing workflow instance lease.
    /// </summary>
    Task RenewLeaseAsync(
        string instanceId,
        WorkflowLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a workflow instance lease held by the specified owner.
    /// </summary>
    Task ReleaseLeaseAsync(
        string instanceId,
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a step execution record before or during execution.
    /// </summary>
    Task AppendStepExecutionAsync(
        WorkflowStepExecutionRecord stepExecution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the outcome of a previously persisted step execution.
    /// </summary>
    Task UpdateStepExecutionAsync(
        WorkflowStepExecutionRecord stepExecution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists persisted step executions for a workflow instance.
    /// </summary>
    Task<IReadOnlyList<WorkflowStepExecutionRecord>> ListStepExecutionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an external event to the workflow event buffer.
    /// </summary>
    Task AppendEventAsync(
        WorkflowEventRecord workflowEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists external events that have not yet been consumed by the workflow runner.
    /// </summary>
    Task<IReadOnlyList<WorkflowEventRecord>> ListUnconsumedEventsAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an external event as consumed by a specific workflow state.
    /// </summary>
    Task MarkEventConsumedAsync(
        string instanceId,
        string eventId,
        string consumedByState,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default);
}
