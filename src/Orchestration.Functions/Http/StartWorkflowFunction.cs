using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Orchestration.Core.Models;
using Orchestration.Functions.Orchestrators;

namespace Orchestration.Functions.Http;

/// <summary>
/// Request body for starting a workflow.
/// </summary>
public sealed class StartWorkflowRequest
{
    public required string WorkflowType { get; init; }
    public string? Version { get; init; }
    public required string EntityId { get; init; }
    public Dictionary<string, object?>? Data { get; init; }
    public string? InstanceId { get; init; }
    public string? CorrelationId { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Response for a started workflow.
/// </summary>
public sealed class StartWorkflowResponse
{
    public required string InstanceId { get; init; }
    public required string StatusUri { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// HTTP endpoint to start a new workflow.
/// </summary>
public class StartWorkflowFunction
{
    private readonly ILogger<StartWorkflowFunction> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StartWorkflowFunction(ILogger<StartWorkflowFunction> logger)
    {
        _logger = logger;
    }

    [Function("StartWorkflow")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows")]
        HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        _logger.LogInformation("StartWorkflow request received");

        StartWorkflowRequest? request;
        try
        {
            var body = await req.ReadAsStringAsync();
            request = JsonSerializer.Deserialize<StartWorkflowRequest>(body ?? "", JsonOptions);

            if (request == null)
            {
                return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid request body");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in request body");
            return await CreateErrorResponseAsync(req, HttpStatusCode.BadRequest, "Invalid JSON format");
        }

        // Generate or use provided instance ID
        var instanceId = request.InstanceId ?? $"workflow-{request.EntityId}-{Guid.NewGuid():N}";

        // Build workflow input
        var input = new WorkflowInput
        {
            WorkflowType = request.WorkflowType,
            Version = request.Version,
            EntityId = request.EntityId,
            Data = request.Data,
            CorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString(),
            IdempotencyKey = request.IdempotencyKey
        };

        try
        {
            // Start the orchestration
            var actualInstanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(WorkflowOrchestrator),
                input);

            // Override instance ID if we want a custom one
            if (!string.IsNullOrEmpty(request.InstanceId))
            {
                instanceId = actualInstanceId;
            }

            _logger.LogInformation(
                "Started workflow {InstanceId} of type {WorkflowType} for entity {EntityId}",
                instanceId, request.WorkflowType, request.EntityId);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new StartWorkflowResponse
            {
                InstanceId = instanceId,
                StatusUri = $"/api/workflows/{instanceId}",
                StartedAt = DateTimeOffset.UtcNow
            });

            response.Headers.Add("Location", $"/api/workflows/{instanceId}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow");
            return await CreateErrorResponseAsync(req, HttpStatusCode.InternalServerError, "Failed to start workflow");
        }
    }

    private static async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string message)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}
