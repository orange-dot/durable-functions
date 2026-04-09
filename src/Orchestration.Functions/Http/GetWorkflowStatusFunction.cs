using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Orchestration.Core.Models;

namespace Orchestration.Functions.Http;

/// <summary>
/// Response for workflow status.
/// </summary>
public sealed class WorkflowStatusResponse
{
    public required string InstanceId { get; init; }
    public required string Status { get; init; }
    public string? WorkflowType { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
    public object? Input { get; init; }
    public object? Output { get; init; }
    public object? CustomStatus { get; init; }
    public string? FailureDetails { get; init; }
}

public sealed class WorkflowListItemResponse
{
    public required string InstanceId { get; init; }
    public required string Status { get; init; }
    public string? WorkflowType { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
}

public sealed class WorkflowListResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("returnedCount")]
    public int ReturnedCount { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("workflows")]
    public IReadOnlyList<WorkflowListItemResponse> Workflows { get; init; } = [];
}

/// <summary>
/// HTTP endpoint to get workflow status.
/// </summary>
public class GetWorkflowStatusFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<GetWorkflowStatusFunction> _logger;

    public GetWorkflowStatusFunction(ILogger<GetWorkflowStatusFunction> logger)
    {
        _logger = logger;
    }

    [Function("GetWorkflowStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows/{instanceId}")]
        HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string instanceId)
    {
        _logger.LogInformation("GetWorkflowStatus request for {InstanceId}", instanceId);

        var metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: true);

        if (metadata == null)
        {
            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteAsJsonAsync(new
            {
                error = "Workflow not found",
                instanceId
            });
            return notFoundResponse;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new WorkflowStatusResponse
        {
            InstanceId = metadata.InstanceId,
            Status = metadata.RuntimeStatus.ToString(),
            WorkflowType = ResolveWorkflowType(metadata),
            CreatedAt = metadata.CreatedAt,
            LastUpdatedAt = metadata.LastUpdatedAt,
            Input = DeserializeJsonValue(metadata.SerializedInput),
            Output = DeserializeJsonValue(metadata.SerializedOutput),
            CustomStatus = DeserializeJsonValue(metadata.SerializedCustomStatus),
            FailureDetails = metadata.FailureDetails?.ErrorMessage
        });

        return response;
    }

    [Function("ListWorkflows")]
    public async Task<HttpResponseData> ListWorkflows(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows")]
        HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("ListWorkflows request");

        var statusFilter = req.Query["status"];
        var requestedPageSize = int.TryParse(req.Query["pageSize"], out var ps)
            ? Math.Clamp(ps, 1, 500)
            : (int?)null;

        var query = new OrchestrationQuery
        {
            PageSize = requestedPageSize ?? 100,
            FetchInputsAndOutputs = true
        };

        if (!string.IsNullOrEmpty(statusFilter) &&
            Enum.TryParse<OrchestrationRuntimeStatus>(statusFilter, true, out var status))
        {
            query = query with { Statuses = [status] };
        }

        var workflows = new List<WorkflowListItemResponse>();
        var totalCount = 0;
        await foreach (var instance in client.GetAllInstancesAsync(query))
        {
            totalCount++;

            if (requestedPageSize.HasValue && workflows.Count >= requestedPageSize.Value)
            {
                continue;
            }

            workflows.Add(new WorkflowListItemResponse
            {
                InstanceId = instance.InstanceId,
                Status = instance.RuntimeStatus.ToString(),
                WorkflowType = ResolveWorkflowType(instance),
                CreatedAt = instance.CreatedAt,
                LastUpdatedAt = instance.LastUpdatedAt
            });
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new WorkflowListResponse
        {
            Count = totalCount,
            ReturnedCount = workflows.Count,
            PageSize = requestedPageSize,
            Workflows = workflows
        });

        return response;
    }

    private static string? ResolveWorkflowType(OrchestrationMetadata metadata)
    {
        var input = DeserializeWorkflowInput(metadata.SerializedInput);
        return string.IsNullOrWhiteSpace(input?.WorkflowType) ? metadata.Name : input.WorkflowType;
    }

    private static WorkflowInput? DeserializeWorkflowInput(string? serializedInput)
    {
        if (string.IsNullOrWhiteSpace(serializedInput))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowInput>(serializedInput, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? DeserializeJsonValue(string? serializedValue)
    {
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<object?>(serializedValue, JsonOptions);
        }
        catch (JsonException)
        {
            return serializedValue;
        }
    }
}
