using System.Text.Json;
using FluentAssertions;
using Orchestration.Core.Models;

namespace Orchestration.Tests.Unit.Core;

public class WorkflowRuntimeValueNormalizerTests
{
    [Fact]
    public void Normalize_WithJsonElementObject_ReturnsCanonicalTree()
    {
        using var document = JsonDocument.Parse("""
        {
            "name": "device-1",
            "count": 42,
            "nested": {
                "values": [1, true, null]
            }
        }
        """);

        var normalized = WorkflowRuntimeValueNormalizer.Normalize(document.RootElement.Clone(), "$.payload")
            .Should().BeOfType<Dictionary<string, object?>>().Subject;

        normalized["name"].Should().Be("device-1");
        normalized["count"].Should().Be(42L);
        var nested = normalized["nested"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        var values = nested["values"].Should().BeOfType<List<object?>>().Subject;
        values.Should().ContainInOrder(1L, true, null);
    }

    [Fact]
    public void Normalize_WithAnonymousObject_ReturnsCanonicalDictionary()
    {
        var normalized = WorkflowRuntimeValueNormalizer.Normalize(
                new
                {
                    DeviceId = "device-123",
                    RetryCount = 3,
                    Tags = new[] { "a", "b" }
                },
                "$.payload")
            .Should().BeOfType<Dictionary<string, object?>>().Subject;

        normalized["deviceId"].Should().Be("device-123");
        normalized["retryCount"].Should().Be(3L);
        normalized["tags"].Should().BeOfType<List<object?>>().Which.Should().ContainInOrder("a", "b");
    }

    [Fact]
    public void NormalizeDictionary_WithNestedCollections_ReturnsDeepNormalizedCopy()
    {
        var input = new Dictionary<string, object?>
        {
            ["count"] = 5,
            ["nested"] = new Dictionary<string, object?>
            {
                ["ratio"] = 1.5m
            },
            ["items"] = new object?[] { 1, "two" }
        };

        var normalized = WorkflowRuntimeValueNormalizer.NormalizeDictionary(input, "$.state")!;

        normalized["count"].Should().Be(5L);
        var nested = normalized["nested"].Should().BeOfType<Dictionary<string, object?>>().Subject;
        nested["ratio"].Should().Be(1.5d);
        var items = normalized["items"].Should().BeOfType<List<object?>>().Subject;
        items.Should().ContainInOrder(1L, "two");
    }

    [Fact]
    public void Normalize_WithNonFiniteDouble_Throws()
    {
        var act = () => WorkflowRuntimeValueNormalizer.Normalize(double.NaN, "$.payload");

        act.Should()
            .Throw<WorkflowRuntimeValueNormalizationException>()
            .WithMessage("*$.payload*finite*");
    }

    [Fact]
    public void DictionaryConverter_Read_WithNonObjectRoot_Throws()
    {
        var act = () => JsonSerializer.Deserialize<WorkflowInput>(
            """
            {
              "workflowType": "Test",
              "entityId": "entity-1",
              "data": [1, 2, 3]
            }
            """);

        var exception = act.Should().Throw<Exception>().Which;
        (exception is JsonException || exception is WorkflowRuntimeValueNormalizationException)
            .Should().BeTrue();
    }
}
