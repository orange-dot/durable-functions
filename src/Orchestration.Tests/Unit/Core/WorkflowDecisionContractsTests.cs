using System.Text.Json;
using FluentAssertions;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow.Interpreter;

namespace Orchestration.Tests.Unit.Core;

public class WorkflowDecisionContractsTests
{
    [Fact]
    public void WorkflowDecision_SerializesPolymorphically()
    {
        WorkflowDecision decision = new ExecuteActivityDecision(
            "CallApi",
            "CallExternalApi",
            new Dictionary<string, object?> { ["deviceId"] = "device-123" },
            TimeoutSeconds: 30);

        var json = JsonSerializer.Serialize(decision);
        var roundTrip = JsonSerializer.Deserialize<WorkflowDecision>(json);
        var document = JsonDocument.Parse(json);

        json.Should().Contain("\"kind\":\"executeActivity\"");
        document.RootElement.EnumerateObject().Count(property => property.NameEquals("kind")).Should().Be(1);
        var executeDecision = roundTrip.Should().BeOfType<ExecuteActivityDecision>().Subject;
        executeDecision.Kind.Should().Be("executeActivity");
        executeDecision.StateName.Should().Be("CallApi");
        executeDecision.ActivityName.Should().Be("CallExternalApi");
        executeDecision.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void WorkflowDecision_RoundTripsNormalizedInputPayload()
    {
        using var document = JsonDocument.Parse("""
        {
            "deviceId": "device-123",
            "count": 2,
            "flags": [true, false]
        }
        """);

        WorkflowDecision decision = new ExecuteActivityDecision(
            "CallApi",
            "CallExternalApi",
            document.RootElement.Clone());

        var json = JsonSerializer.Serialize(decision);
        var roundTrip = JsonSerializer.Deserialize<WorkflowDecision>(json)
            .Should().BeOfType<ExecuteActivityDecision>().Subject;

        var input = roundTrip.Input.Should().BeOfType<Dictionary<string, object?>>().Subject;
        input["deviceId"].Should().Be("device-123");
        input["count"].Should().Be(2L);
        input["flags"].Should().BeOfType<List<object?>>().Which.Should().ContainInOrder(true, false);
    }

    [Fact]
    public void WorkflowDecisionOutcome_SerializesPolymorphically()
    {
        WorkflowDecisionOutcome outcome = new ActivityFailedOutcome(
            "request timed out",
            ErrorCode: "Timeout",
            ErrorType: "TimeoutException");

        var json = JsonSerializer.Serialize(outcome);
        var roundTrip = JsonSerializer.Deserialize<WorkflowDecisionOutcome>(json);
        var document = JsonDocument.Parse(json);

        json.Should().Contain("\"kind\":\"activityFailed\"");
        document.RootElement.EnumerateObject().Count(property => property.NameEquals("kind")).Should().Be(1);
        var failedOutcome = roundTrip.Should().BeOfType<ActivityFailedOutcome>().Subject;
        failedOutcome.Kind.Should().Be("activityFailed");
        failedOutcome.ErrorMessage.Should().Be("request timed out");
        failedOutcome.ErrorCode.Should().Be("Timeout");
        failedOutcome.ErrorType.Should().Be("TimeoutException");
    }

    [Fact]
    public void WorkflowDecisionOutcome_RoundTripsNormalizedPayload()
    {
        using var document = JsonDocument.Parse("""
        {
            "approved": true,
            "attempts": 3
        }
        """);

        WorkflowDecisionOutcome outcome = new EventReceivedOutcome(document.RootElement.Clone());

        var json = JsonSerializer.Serialize(outcome);
        var roundTrip = JsonSerializer.Deserialize<WorkflowDecisionOutcome>(json)
            .Should().BeOfType<EventReceivedOutcome>().Subject;

        var payload = roundTrip.Payload.Should().BeOfType<Dictionary<string, object?>>().Subject;
        payload["approved"].Should().Be(true);
        payload["attempts"].Should().Be(3L);
    }

    [Fact]
    public void WorkflowRuntimeState_RoundTripsPendingDecisionAndCompensationMetadata()
    {
        var state = new WorkflowRuntimeState
        {
            CurrentStep = "WaitForApproval",
            PendingDecision = new WaitForEventDecision(
                "WaitForApproval",
                "ApprovalReceived",
                new DateTimeOffset(2026, 04, 08, 12, 30, 00, TimeSpan.Zero)),
            IsCompensating = true,
            CompensationStateName = "Rollback",
            CompensationStepIndex = 2,
            Error = new WorkflowError
            {
                Message = "boom",
                Code = "FAILED",
                StepName = "CallApi"
            }
        };
        state.CompletedCompensationSteps.Add("UndoCreate");

        var json = JsonSerializer.Serialize(state);
        var roundTrip = JsonSerializer.Deserialize<WorkflowRuntimeState>(json);

        roundTrip.Should().NotBeNull();
        roundTrip!.CurrentStep.Should().Be("WaitForApproval");
        roundTrip.IsCompensating.Should().BeTrue();
        roundTrip.CompensationStateName.Should().Be("Rollback");
        roundTrip.CompensationStepIndex.Should().Be(2);
        roundTrip.CompletedCompensationSteps.Should().ContainSingle().Which.Should().Be("UndoCreate");
        roundTrip.PendingDecision.Should().BeOfType<WaitForEventDecision>();
        roundTrip.Error.Should().NotBeNull();
        roundTrip.Error!.Code.Should().Be("FAILED");
    }

    [Fact]
    public void WorkflowRuntimeState_RoundTripsNormalizedDynamicValues()
    {
        using var document = JsonDocument.Parse("""
        {
            "approved": true,
            "score": 99
        }
        """);

        var state = new WorkflowRuntimeState
        {
            Input = new WorkflowInput
            {
                WorkflowType = "Approval",
                EntityId = "entity-1",
                Data = new Dictionary<string, object?> { ["payload"] = document.RootElement.Clone() }
            }
        };
        state.Variables["payload"] = document.RootElement.Clone();
        state.StepResults["step1"] = new { Count = 2 };

        var json = JsonSerializer.Serialize(state);
        var roundTrip = JsonSerializer.Deserialize<WorkflowRuntimeState>(json);

        roundTrip.Should().NotBeNull();
        var inputPayload = roundTrip!.Input!.Data!["payload"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        inputPayload["approved"].Should().Be(true);
        inputPayload["score"].Should().Be(99L);

        var variablePayload = roundTrip.Variables["payload"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        variablePayload["approved"].Should().Be(true);
        variablePayload["score"].Should().Be(99L);

        var step1 = roundTrip.StepResults["step1"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        step1["count"].Should().Be(2L);
    }
}
