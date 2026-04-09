using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;

namespace Orchestration.Functions.Activities.Database;

/// <summary>
/// Input for creating a record.
/// </summary>
public sealed class CreateRecordInput
{
    public required string RecordType { get; init; }
    public required string IdempotencyKey { get; init; }
    public required Dictionary<string, object?> Data { get; init; }
    public IReadOnlyList<CapabilityGrant>? CapabilityGrants { get; init; }
}

/// <summary>
/// Output from creating a record.
/// </summary>
public sealed record CreateRecordOutput
{
    public required string RecordId { get; init; }
    public required string RecordType { get; init; }
    public bool WasExisting { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Activity that creates a new record with idempotency support.
/// </summary>
public class CreateRecordActivity
{
    private readonly IWorkflowRepository _repository;
    private readonly IActivityCapabilityScopeFactory _scopeFactory;
    private readonly ILogger<CreateRecordActivity> _logger;

    public CreateRecordActivity(
        IWorkflowRepository repository,
        IActivityCapabilityScopeFactory scopeFactory,
        ILogger<CreateRecordActivity> logger)
    {
        _repository = repository;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Function(nameof(CreateRecordActivity))]
    public async Task<CreateRecordOutput> Run([ActivityTrigger] CreateRecordInput input)
    {
        _logger.LogInformation(
            "Creating record of type {RecordType} with idempotency key {IdempotencyKey}.",
            input.RecordType, input.IdempotencyKey);

        // Check for existing idempotent result
        var existing = await _repository.GetIdempotencyRecordAsync<CreateRecordOutput>(input.IdempotencyKey);
        if (existing != null)
        {
            _logger.LogInformation(
                "Found existing record for idempotency key {IdempotencyKey}. Returning cached result.",
                input.IdempotencyKey);
            return existing with { WasExisting = true };
        }

        // Create the record
        var recordId = Guid.NewGuid().ToString();
        var record = new Dictionary<string, object?>(input.Data)
        {
            ["id"] = recordId,
            ["recordType"] = input.RecordType,
            ["createdAt"] = DateTimeOffset.UtcNow,
            ["idempotencyKey"] = input.IdempotencyKey
        };

        var table = DatabaseActivityCapabilityResolver.ResolveReadWriteTable(
            _scopeFactory,
            input.RecordType,
            input.CapabilityGrants);

        var storedRecord = await table.InsertAsync(record);
        recordId = storedRecord["id"]?.ToString() ?? recordId;

        var result = new CreateRecordOutput
        {
            RecordId = recordId,
            RecordType = input.RecordType,
            WasExisting = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Store idempotency record
        await _repository.SaveIdempotencyRecordAsync(input.IdempotencyKey, result);

        _logger.LogInformation(
            "Created record {RecordId} of type {RecordType}.",
            recordId, input.RecordType);

        return result;
    }
}
