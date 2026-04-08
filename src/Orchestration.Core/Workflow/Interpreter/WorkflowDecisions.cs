using System.Text.Json.Serialization;

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
    object? Input,
    global::Orchestration.Core.Workflow.RetryPolicy? Retry = null,
    int? TimeoutSeconds = null,
    bool IsCompensation = false,
    int? CompensationStepIndex = null,
    string? CompensationStepName = null) : WorkflowDecision(StateName)
{
    public override string Kind => "executeActivity";
}

public sealed record WaitForEventDecision(
    string StateName,
    string EventName,
    DateTimeOffset? TimeoutAt = null) : WorkflowDecision(StateName)
{
    public override string Kind => "waitForEvent";
}

public sealed record DelayUntilDecision(
    string StateName,
    DateTimeOffset FireAt) : WorkflowDecision(StateName)
{
    public override string Kind => "delayUntil";
}

public sealed record CompleteWorkflowDecision(
    string StateName,
    object? Output = null) : WorkflowDecision(StateName)
{
    public override string Kind => "completeWorkflow";
}

public sealed record FailWorkflowDecision(
    string StateName,
    string ErrorCode,
    string ErrorMessage) : WorkflowDecision(StateName)
{
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
    object? Output = null) : WorkflowDecisionOutcome
{
    public override string Kind => "activityCompleted";
}

public sealed record ActivityFailedOutcome(
    string ErrorMessage,
    string? ErrorCode = null,
    string? ErrorType = null,
    string? StackTrace = null) : WorkflowDecisionOutcome
{
    public override string Kind => "activityFailed";
}

public sealed record EventReceivedOutcome(
    object? Payload = null) : WorkflowDecisionOutcome
{
    public override string Kind => "eventReceived";
}

public sealed record DelayTimedOutOutcome : WorkflowDecisionOutcome
{
    public override string Kind => "delayTimedOut";
}
