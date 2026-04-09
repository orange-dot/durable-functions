using System.Collections;
using System.Text.Json;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Orchestration.Core.Models;
using Orchestration.Supabase.Internal;

namespace Orchestration.Tests.Unit.Infrastructure;

public sealed class SupabaseJsonTests
{
    [Fact]
    public void SerializeRuntimeValue_RoundTripsEdgeCases_WithoutSerializerArtifacts()
    {
        var timestamp = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.FromHours(5.5));
        var runtimeState = new WorkflowRuntimeState
        {
            Input = new WorkflowInput
            {
                WorkflowType = "DeviceOnboarding",
                EntityId = "device-123",
                CorrelationId = "corr-123",
                Data = new Dictionary<string, object?>
                {
                    ["nullable"] = null
                }
            },
            Variables = new Dictionary<string, object?>
            {
                ["timestamp"] = timestamp,
                ["boundary"] = long.MaxValue,
                ["nested"] = new Dictionary<string, object?>
                {
                    ["nullable"] = null,
                    ["items"] = new List<object?> { 1L, null, "ready" }
                }
            },
            StepResults = new Dictionary<string, object?>
            {
                ["step-1"] = new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["nullable"] = null
                }
            },
            ExecutedSteps =
            [
                new ExecutedStep
                {
                    StepName = "step-1",
                    StepType = "Task",
                    ExecutedAt = timestamp,
                    ActivityName = "CreateRecord",
                    CompensationActivity = "DeleteRecord",
                    Input = new Dictionary<string, object?>
                    {
                        ["recordId"] = "rec-1",
                        ["nullable"] = null
                    },
                    Output = new Dictionary<string, object?>
                    {
                        ["createdAt"] = timestamp
                    }
                }
            ],
            System = new SystemValues
            {
                InstanceId = "instance-123",
                StartTime = timestamp,
                CurrentTime = timestamp
            }
        };

        var serialized = SupabaseJson.SerializeRuntimeValue(runtimeState);
        var roundTripped = SupabaseJson.DeserializeRuntimeValue<WorkflowRuntimeState>(serialized);

        roundTripped.System.StartTime.Should().Be(timestamp);
        roundTripped.System.CurrentTime.Should().Be(timestamp);
        roundTripped.ExecutedSteps.Should().ContainSingle();
        roundTripped.ExecutedSteps[0].ExecutedAt.Should().Be(timestamp);
        roundTripped.Variables["boundary"].Should().Be(long.MaxValue);

        var nested = roundTripped.Variables["nested"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        nested["nullable"].Should().BeNull();

        var items = nested["items"].Should().BeOfType<List<object?>>().Subject;
        items.Should().ContainInOrder(1L, null, "ready");

        AssertContainsNoSerializerArtifacts(roundTripped);
    }

    private static void AssertContainsNoSerializerArtifacts(object? value)
    {
        switch (value)
        {
            case null:
                return;
            case JsonElement:
                throw new Xunit.Sdk.XunitException("Runtime payload contained an unexpected JsonElement instance.");
            case JToken:
                throw new Xunit.Sdk.XunitException("Runtime payload contained an unexpected JToken instance.");
            case WorkflowRuntimeState state:
                AssertContainsNoSerializerArtifacts(state.Input);
                AssertContainsNoSerializerArtifacts(state.Variables);
                AssertContainsNoSerializerArtifacts(state.StepResults);
                foreach (var step in state.ExecutedSteps)
                {
                    AssertContainsNoSerializerArtifacts(step);
                }
                AssertContainsNoSerializerArtifacts(state.Error);
                return;
            case WorkflowInput input:
                AssertContainsNoSerializerArtifacts(input.Data);
                return;
            case ExecutedStep step:
                AssertContainsNoSerializerArtifacts(step.Input);
                AssertContainsNoSerializerArtifacts(step.Output);
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    AssertContainsNoSerializerArtifacts(entry.Value);
                }
                return;
            case IEnumerable enumerable when value is not string:
                foreach (var item in enumerable)
                {
                    AssertContainsNoSerializerArtifacts(item);
                }
                return;
            default:
                return;
        }
    }
}
