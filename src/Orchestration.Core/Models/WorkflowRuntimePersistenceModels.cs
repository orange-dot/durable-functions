using System.Text.Json.Serialization;
using Orchestration.Core.Workflow.Interpreter;

namespace Orchestration.Core.Models;

/// <summary>
/// Durable lifecycle states for a workflow instance in a non-replay runtime.
/// </summary>
public enum WorkflowInstanceStatus
{
    Pending,
    Running,
    Waiting,
    Compensating,
    Completed,
    Failed,
    Compensated
}

/// <summary>
/// Durable lifecycle states for a persisted step execution record.
/// </summary>
public enum StepExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Lease ownership metadata for a workflow instance claim.
/// </summary>
public sealed class WorkflowLease
{
    [JsonPropertyName("ownerId")]
    public required string OwnerId { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Authoritative persisted snapshot of a workflow instance for non-replay runtimes.
/// </summary>
public sealed class WorkflowInstanceRecord
{
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("definitionId")]
    public required string DefinitionId { get; init; }

    [JsonPropertyName("definitionVersion")]
    public string? DefinitionVersion { get; init; }

    [JsonPropertyName("status")]
    public WorkflowInstanceStatus Status { get; set; }

    [JsonPropertyName("currentStateName")]
    public string? CurrentStateName { get; set; }

    [JsonPropertyName("runtimeState")]
    public required WorkflowRuntimeState RuntimeState { get; init; }

    [JsonPropertyName("lease")]
    public WorkflowLease? Lease { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Persisted activity or wait boundary execution record.
/// </summary>
public sealed class WorkflowStepExecutionRecord
{
    [JsonPropertyName("stepExecutionId")]
    public required string StepExecutionId { get; init; }

    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("stateName")]
    public required string StateName { get; init; }

    [JsonPropertyName("activityName")]
    public string? ActivityName { get; init; }

    [JsonPropertyName("attempt")]
    public int Attempt { get; set; } = 1;

    [JsonPropertyName("isCompensation")]
    public bool IsCompensation { get; init; }

    [JsonPropertyName("status")]
    public StepExecutionStatus Status { get; set; }

    [JsonPropertyName("decision")]
    public required WorkflowDecision Decision { get; init; }

    [JsonPropertyName("outcome")]
    public WorkflowDecisionOutcome? Outcome { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTimeOffset? FinishedAt { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Persisted external event buffered for workflow resumption.
/// </summary>
public sealed class WorkflowEventRecord
{
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("eventName")]
    public required string EventName { get; init; }

    [JsonPropertyName("payload")]
    [JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    public object? Payload { get; init; }

    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("consumedAt")]
    public DateTimeOffset? ConsumedAt { get; set; }

    [JsonPropertyName("consumedByState")]
    public string? ConsumedByState { get; set; }
}
