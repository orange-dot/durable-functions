using System.Text.Json.Serialization;
using Orchestration.Core.Workflow.Interpreter;

namespace Orchestration.Core.Models;

/// <summary>
/// Runtime state maintained during workflow execution.
/// </summary>
public sealed class WorkflowRuntimeState
{
    /// <summary>
    /// The original workflow input.
    /// </summary>
    [JsonPropertyName("input")]
    public WorkflowInput? Input { get; set; }

    /// <summary>
    /// State variables accumulated during execution.
    /// </summary>
    [JsonPropertyName("variables")]
    [JsonConverter(typeof(WorkflowRuntimeValueDictionaryJsonConverter))]
    public Dictionary<string, object?> Variables { get; set; } = new();

    /// <summary>
    /// Results from completed steps, keyed by step name.
    /// </summary>
    [JsonPropertyName("stepResults")]
    [JsonConverter(typeof(WorkflowRuntimeValueDictionaryJsonConverter))]
    public Dictionary<string, object?> StepResults { get; set; } = new();

    /// <summary>
    /// History of executed steps for compensation.
    /// </summary>
    [JsonPropertyName("executedSteps")]
    public List<ExecutedStep> ExecutedSteps { get; set; } = new();

    /// <summary>
    /// Current step being executed.
    /// </summary>
    [JsonPropertyName("currentStep")]
    public string? CurrentStep { get; set; }

    /// <summary>
    /// Pending persisted decision awaiting execution or outcome application.
    /// </summary>
    [JsonPropertyName("pendingDecision")]
    public WorkflowDecision? PendingDecision { get; set; }

    /// <summary>
    /// Whether the workflow is currently executing compensation logic.
    /// </summary>
    [JsonPropertyName("isCompensating")]
    public bool IsCompensating { get; set; }

    /// <summary>
    /// Name of the active compensation state, when compensation mode is enabled.
    /// </summary>
    [JsonPropertyName("compensationStateName")]
    public string? CompensationStateName { get; set; }

    /// <summary>
    /// Zero-based cursor for the next compensation step to evaluate.
    /// </summary>
    [JsonPropertyName("compensationStepIndex")]
    public int CompensationStepIndex { get; set; }

    /// <summary>
    /// Names of compensation steps that completed successfully.
    /// </summary>
    [JsonPropertyName("completedCompensationSteps")]
    public List<string> CompletedCompensationSteps { get; set; } = new();

    /// <summary>
    /// Error information if the workflow encountered an error.
    /// </summary>
    [JsonPropertyName("error")]
    public WorkflowError? Error { get; set; }

    /// <summary>
    /// System-provided values accessible via $.system.*
    /// </summary>
    [JsonPropertyName("system")]
    public SystemValues System { get; set; } = new();
}

/// <summary>
/// Record of an executed step for compensation tracking.
/// </summary>
public sealed class ExecutedStep
{
    [JsonPropertyName("stepName")]
    public required string StepName { get; init; }

    [JsonPropertyName("stepType")]
    public required string StepType { get; init; }

    [JsonPropertyName("executedAt")]
    public DateTimeOffset ExecutedAt { get; init; }

    [JsonPropertyName("activityName")]
    public string? ActivityName { get; init; }

    [JsonPropertyName("compensationActivity")]
    public string? CompensationActivity { get; init; }

    [JsonPropertyName("input")]
    [JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    public object? Input { get; init; }

    [JsonPropertyName("output")]
    [JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    public object? Output { get; init; }
}

/// <summary>
/// Error information captured during workflow execution.
/// </summary>
public sealed class WorkflowError
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("stepName")]
    public string? StepName { get; init; }

    [JsonPropertyName("activityName")]
    public string? ActivityName { get; init; }

    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }
}

/// <summary>
/// System-provided values accessible during workflow execution.
/// </summary>
public sealed class SystemValues
{
    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; set; }

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("currentTime")]
    public DateTimeOffset CurrentTime { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }
}
