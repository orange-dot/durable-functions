using FluentAssertions;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow;
using Orchestration.Core.Workflow.Interpreter;
using Orchestration.Core.Workflow.StateTypes;

namespace Orchestration.Tests.Unit.Core;

public class WorkflowDecisionInterpreterTests
{
    private readonly WorkflowInterpreter _interpreter = new(new JsonPathResolver());

    [Fact]
    public void EvaluateNext_WithChoiceState_CollapsesToTaskDecision()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CheckStatus",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CheckStatus"] = new ChoiceStateDefinition
                {
                    Choices =
                    [
                        new ChoiceRule
                        {
                            Condition = new ComparisonCondition
                            {
                                Variable = "$.variables.status",
                                ComparisonType = ComparisonType.Equals,
                                Value = "approved"
                            },
                            Next = "CallApi"
                        }
                    ],
                    Default = "FailEnd"
                },
                ["CallApi"] = new TaskStateDefinition
                {
                    Activity = "CallExternalApi",
                    Input = new Dictionary<string, object?> { ["status"] = "$.variables.status" },
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition(),
                ["FailEnd"] = new FailStateDefinition { Error = "Rejected", Cause = "Rejected" }
            }
        };

        var state = CreateState();
        state.Variables["status"] = "approved";

        var decision = _interpreter.EvaluateNext(workflow, state);

        var executeDecision = decision.Should().BeOfType<ExecuteActivityDecision>().Subject;
        executeDecision.StateName.Should().Be("CallApi");
        executeDecision.ActivityName.Should().Be("CallExternalApi");
        executeDecision.Input.Should().BeEquivalentTo(new Dictionary<string, object?> { ["status"] = "approved" });
        state.CurrentStep.Should().Be("CallApi");
        state.PendingDecision.Should().BeSameAs(decision);
    }

    [Fact]
    public void EvaluateNext_WithExternalEventWait_ReturnsWaitForEventDecisionWithAbsoluteTimeout()
    {
        var now = new DateTimeOffset(2026, 04, 08, 12, 00, 00, TimeSpan.Zero);
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "WaitForApproval",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["WaitForApproval"] = new WaitStateDefinition
                {
                    ExternalEvent = new ExternalEventWait
                    {
                        EventName = "ApprovalReceived",
                        TimeoutSeconds = 30,
                        ResultPath = "$.variables.approval"
                    },
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState(now);

        var decision = _interpreter.EvaluateNext(workflow, state);

        decision.Should().BeEquivalentTo(
            new WaitForEventDecision("WaitForApproval", "ApprovalReceived", now.AddSeconds(30)));
        state.PendingDecision.Should().BeSameAs(decision);
    }

    [Fact]
    public void EvaluateNext_WithDelayWait_ReturnsDelayUntilDecision()
    {
        var now = new DateTimeOffset(2026, 04, 08, 12, 00, 00, TimeSpan.Zero);
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "WaitStep",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["WaitStep"] = new WaitStateDefinition
                {
                    Seconds = 45,
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState(now);

        var decision = _interpreter.EvaluateNext(workflow, state);

        decision.Should().BeEquivalentTo(new DelayUntilDecision("WaitStep", now.AddSeconds(45)));
        state.PendingDecision.Should().BeSameAs(decision);
    }

    [Fact]
    public void EvaluateNext_WithParallelState_ThrowsNotSupportedException()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "ParallelStep",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["ParallelStep"] = new ParallelStateDefinition
                {
                    Branches =
                    [
                        new ParallelBranch
                        {
                            Name = "BranchA",
                            StartAt = "BranchStart",
                            States = new Dictionary<string, WorkflowStateDefinition>
                            {
                                ["BranchStart"] = new SucceedStateDefinition()
                            }
                        }
                    ]
                }
            }
        };

        var state = CreateState();

        var act = () => _interpreter.EvaluateNext(workflow, state);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Parallel states are not supported*");
    }

    [Fact]
    public void ApplyOutcome_WithActivityCompleted_StoresResultAndAdvances()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CallApi",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CallApi"] = new TaskStateDefinition
                {
                    Activity = "CallExternalApi",
                    ResultPath = "$.stepResults.apiResult",
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        var decision = _interpreter.EvaluateNext(workflow, state);

        _interpreter.ApplyOutcome(
            workflow,
            state,
            decision,
            new ActivityCompletedOutcome(new Dictionary<string, object?> { ["response"] = "ok" }));

        state.PendingDecision.Should().BeNull();
        state.CurrentStep.Should().Be("SuccessEnd");
        state.StepResults.Should().ContainKey("apiResult");
        state.StepResults["apiResult"].Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["response"] = "ok" });

        var terminalDecision = _interpreter.EvaluateNext(workflow, state);
        terminalDecision.Should().BeOfType<CompleteWorkflowDecision>();
    }

    [Fact]
    public void ApplyOutcome_WithActivityFailureAndCatch_SetsCatchPayloadAndAdvances()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CallApi",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CallApi"] = new TaskStateDefinition
                {
                    Activity = "CallExternalApi",
                    Catch =
                    [
                        new CatchDefinition
                        {
                            Errors = ["TimeoutException"],
                            ResultPath = "$.variables.lastError",
                            Next = "Recover"
                        }
                    ]
                },
                ["Recover"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        var decision = _interpreter.EvaluateNext(workflow, state);

        _interpreter.ApplyOutcome(
            workflow,
            state,
            decision,
            new ActivityFailedOutcome("request timed out", ErrorType: "TimeoutException"));

        state.PendingDecision.Should().BeNull();
        state.CurrentStep.Should().Be("Recover");
        state.Error.Should().NotBeNull();
        state.Error!.StepName.Should().Be("CallApi");
        state.Variables.Should().ContainKey("lastError");
        state.Variables["lastError"].Should().BeEquivalentTo(
            new Dictionary<string, object?>
            {
                ["error"] = "TimeoutException",
                ["message"] = "request timed out"
            });
    }

    [Fact]
    public void ApplyOutcome_WithActivityFailure_EntersCompensationMode()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CreateRecord",
            Config = new WorkflowConfiguration
            {
                CompensationState = "Rollback"
            },
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CreateRecord"] = new TaskStateDefinition
                {
                    Activity = "CreateRecordActivity",
                    CompensateWith = "DeleteRecordActivity",
                    Next = "SuccessEnd"
                },
                ["Rollback"] = new CompensationStateDefinition
                {
                    Steps =
                    [
                        new CompensationStep
                        {
                            Name = "UndoCreate",
                            Activity = "DeleteRecordActivity",
                            Input = new Dictionary<string, object?> { ["id"] = "123" }
                        }
                    ],
                    FinalState = "FailEnd"
                },
                ["FailEnd"] = new FailStateDefinition
                {
                    Error = "WorkflowFailed",
                    Cause = "Workflow failed"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        var decision = _interpreter.EvaluateNext(workflow, state);

        _interpreter.ApplyOutcome(
            workflow,
            state,
            decision,
            new ActivityFailedOutcome("boom", ErrorCode: "ExternalFailure", ErrorType: "HttpRequestException"));

        state.PendingDecision.Should().BeNull();
        state.IsCompensating.Should().BeTrue();
        state.CompensationStateName.Should().Be("Rollback");
        state.CompensationStepIndex.Should().Be(0);
        state.CurrentStep.Should().Be("Rollback");

        var compensationDecision = _interpreter.EvaluateNext(workflow, state);
        compensationDecision.Should().BeEquivalentTo(
            new ExecuteActivityDecision(
                "Rollback",
                "DeleteRecordActivity",
                new Dictionary<string, object?> { ["id"] = "123" },
                Retry: null,
                TimeoutSeconds: null,
                IsCompensation: true,
                CompensationStepIndex: 0,
                CompensationStepName: "UndoCreate"));
    }

    [Fact]
    public void ApplyOutcome_WithMismatchedPendingDecision_Throws()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CallApi",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CallApi"] = new TaskStateDefinition
                {
                    Activity = "CallExternalApi",
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        _ = _interpreter.EvaluateNext(workflow, state);

        var act = () => _interpreter.ApplyOutcome(
            workflow,
            state,
            new ExecuteActivityDecision("OtherStep", "CallExternalApi", null),
            new ActivityCompletedOutcome());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*while 'executeActivity' on state 'CallApi' is pending*");
    }

    [Fact]
    public void ApplyOutcome_WithCompensationSuccess_AdvancesCursorAndTransitionsToFinalFail()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CreateRecord",
            Config = new WorkflowConfiguration
            {
                CompensationState = "Rollback"
            },
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CreateRecord"] = new TaskStateDefinition
                {
                    Activity = "CreateRecordActivity",
                    Next = "SuccessEnd"
                },
                ["Rollback"] = new CompensationStateDefinition
                {
                    Steps =
                    [
                        new CompensationStep
                        {
                            Name = "UndoCreate",
                            Activity = "DeleteRecordActivity"
                        }
                    ],
                    FinalState = "FailEnd"
                },
                ["FailEnd"] = new FailStateDefinition
                {
                    Error = "WorkflowFailed",
                    Cause = "Rollback complete"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        state.Error = new WorkflowError
        {
            Message = "original failure",
            Code = "OriginalFailure",
            StepName = "CreateRecord"
        };
        state.IsCompensating = true;
        state.CompensationStateName = "Rollback";
        state.CurrentStep = "Rollback";

        var compensationDecision = (ExecuteActivityDecision)_interpreter.EvaluateNext(workflow, state);

        _interpreter.ApplyOutcome(
            workflow,
            state,
            compensationDecision,
            new ActivityCompletedOutcome());

        state.PendingDecision.Should().BeNull();
        state.CurrentStep.Should().Be("Rollback");
        state.CompensationStepIndex.Should().Be(1);
        state.CompletedCompensationSteps.Should().Contain("UndoCreate");

        var nextDecision = _interpreter.EvaluateNext(workflow, state);
        var failDecision = nextDecision.Should().BeOfType<FailWorkflowDecision>().Subject;
        failDecision.StateName.Should().Be("FailEnd");
        failDecision.ErrorCode.Should().Be("WorkflowFailed");
        state.IsCompensating.Should().BeFalse();
        state.CompensationStateName.Should().BeNull();
        state.CurrentStep.Should().Be("FailEnd");
    }

    [Fact]
    public void ApplyOutcome_WithUncaughtActivityFailure_ProducesFailDecisionOnNextEvaluation()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "test-workflow",
            Name = "Test Workflow",
            StartAt = "CallApi",
            States = new Dictionary<string, WorkflowStateDefinition>
            {
                ["CallApi"] = new TaskStateDefinition
                {
                    Activity = "CallExternalApi",
                    Next = "SuccessEnd"
                },
                ["SuccessEnd"] = new SucceedStateDefinition()
            }
        };

        var state = CreateState();
        var decision = _interpreter.EvaluateNext(workflow, state);

        _interpreter.ApplyOutcome(
            workflow,
            state,
            decision,
            new ActivityFailedOutcome("request failed", ErrorCode: "HttpFailure", ErrorType: "HttpRequestException"));

        state.PendingDecision.Should().BeNull();
        state.CurrentStep.Should().Be("CallApi");
        state.Error.Should().NotBeNull();

        var nextDecision = _interpreter.EvaluateNext(workflow, state);
        nextDecision.Should().BeEquivalentTo(
            new FailWorkflowDecision("CallApi", "HttpFailure", "request failed"));
    }

    private static WorkflowRuntimeState CreateState(DateTimeOffset? now = null)
    {
        var currentTime = now ?? new DateTimeOffset(2026, 04, 08, 12, 00, 00, TimeSpan.Zero);

        return new WorkflowRuntimeState
        {
            Variables = new Dictionary<string, object?>(),
            StepResults = new Dictionary<string, object?>(),
            System = new SystemValues
            {
                InstanceId = "test-instance",
                StartTime = currentTime,
                CurrentTime = currentTime
            }
        };
    }
}
