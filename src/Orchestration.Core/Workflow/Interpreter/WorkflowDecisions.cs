using System.Text.Json.Serialization;
using Orchestration.Core.Models;

namespace Orchestration.Core.Workflow.Interpreter;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExecuteActivityDecision), "executeActivity")]
[JsonDerivedType(typeof(WaitForEventDecision), "waitForEvent")]
[JsonDerivedType(typeof(DelayUntilDecision), "delayUntil")]
[JsonDerivedType(typeof(CompleteWorkflowDecision), "completeWorkflow")]
[JsonDerivedType(typeof(FailWorkflowDecision), "failWorkflow")]
public abstract record WorkflowDecision(string StateName)
{
    [JsonIgnore]
    public abstract string Kind { get; }
}

public sealed record ExecuteActivityDecision(
    string StateName,
    string ActivityName,
    [property: JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    object? Input,
    global::Orchestration.Core.Workflow.RetryPolicy? Retry = null,
    int? TimeoutSeconds = null,
    bool IsCompensation = false,
    int? CompensationStepIndex = null,
    string? CompensationStepName = null) : WorkflowDecision(StateName)
{
    [JsonIgnore]
    public override string Kind => "executeActivity";
}

public sealed record WaitForEventDecision(
    string StateName,
    string EventName,
    DateTimeOffset? TimeoutAt = null) : WorkflowDecision(StateName)
{
    [JsonIgnore]
    public override string Kind => "waitForEvent";
}

public sealed record DelayUntilDecision(
    string StateName,
    DateTimeOffset FireAt) : WorkflowDecision(StateName)
{
    [JsonIgnore]
    public override string Kind => "delayUntil";
}

public sealed record CompleteWorkflowDecision(
    string StateName,
    [property: JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    object? Output = null) : WorkflowDecision(StateName)
{
    [JsonIgnore]
    public override string Kind => "completeWorkflow";
}

public sealed record FailWorkflowDecision(
    string StateName,
    string ErrorCode,
    string ErrorMessage) : WorkflowDecision(StateName)
{
    [JsonIgnore]
    public override string Kind => "failWorkflow";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ActivityCompletedOutcome), "activityCompleted")]
[JsonDerivedType(typeof(ActivityFailedOutcome), "activityFailed")]
[JsonDerivedType(typeof(EventReceivedOutcome), "eventReceived")]
[JsonDerivedType(typeof(DelayTimedOutOutcome), "delayTimedOut")]
public abstract record WorkflowDecisionOutcome
{
    [JsonIgnore]
    public abstract string Kind { get; }
}

public sealed record ActivityCompletedOutcome(
    [property: JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    object? Output = null) : WorkflowDecisionOutcome
{
    [JsonIgnore]
    public override string Kind => "activityCompleted";
}

public sealed record ActivityFailedOutcome(
    string ErrorMessage,
    string? ErrorCode = null,
    string? ErrorType = null,
    string? StackTrace = null) : WorkflowDecisionOutcome
{
    [JsonIgnore]
    public override string Kind => "activityFailed";
}

public sealed record EventReceivedOutcome(
    [property: JsonConverter(typeof(WorkflowRuntimeValueJsonConverter))]
    object? Payload = null) : WorkflowDecisionOutcome
{
    [JsonIgnore]
    public override string Kind => "eventReceived";
}

public sealed record DelayTimedOutOutcome : WorkflowDecisionOutcome
{
    [JsonIgnore]
    public override string Kind => "delayTimedOut";
}
