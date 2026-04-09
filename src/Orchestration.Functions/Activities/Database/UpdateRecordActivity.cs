using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;

namespace Orchestration.Functions.Activities.Database;

/// <summary>
/// Input for updating a record.
/// </summary>
public sealed class UpdateRecordInput
{
    public required string RecordId { get; init; }
    public required string RecordType { get; init; }
    public required Dictionary<string, object?> Updates { get; init; }
    public string? IdempotencyKey { get; init; }
    public IReadOnlyList<CapabilityGrant>? CapabilityGrants { get; init; }
}

/// <summary>
/// Output from updating a record.
/// </summary>
public sealed class UpdateRecordOutput
{
    public required string RecordId { get; init; }
    public bool Success { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public Dictionary<string, object?>? PreviousValues { get; init; }
}

/// <summary>
/// Activity that updates an existing record.
/// </summary>
public class UpdateRecordActivity
{
    private readonly IWorkflowRepository _repository;
    private readonly IActivityCapabilityScopeFactory _scopeFactory;
    private readonly ILogger<UpdateRecordActivity> _logger;

    public UpdateRecordActivity(
        IWorkflowRepository repository,
        IActivityCapabilityScopeFactory scopeFactory,
        ILogger<UpdateRecordActivity> logger)
    {
        _repository = repository;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Function(nameof(UpdateRecordActivity))]
    public async Task<UpdateRecordOutput> Run([ActivityTrigger] UpdateRecordInput input)
    {
        _logger.LogInformation(
            "Updating record {RecordId} of type {RecordType}.",
            input.RecordId, input.RecordType);

        // Check idempotency if key provided
        if (!string.IsNullOrEmpty(input.IdempotencyKey))
        {
            var existing = await _repository.GetIdempotencyRecordAsync<UpdateRecordOutput>(input.IdempotencyKey);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Found existing update for idempotency key {IdempotencyKey}.",
                    input.IdempotencyKey);
                return existing;
            }
        }

        // Get current record for compensation support
        var table = DatabaseActivityCapabilityResolver.ResolveReadWriteTable(
            _scopeFactory,
            input.RecordType,
            input.CapabilityGrants);

        var currentRecord = await table.GetByIdAsync(input.RecordId);
        if (currentRecord == null)
        {
            throw new InvalidOperationException($"Record {input.RecordId} not found.");
        }

        // Store previous values for potential rollback
        var previousValues = new Dictionary<string, object?>();
        foreach (var key in input.Updates.Keys)
        {
            if (currentRecord.TryGetValue(key, out var value))
            {
                previousValues[key] = value;
            }
        }

        // Apply updates
        foreach (var kvp in input.Updates)
        {
            currentRecord[kvp.Key] = kvp.Value;
        }
        currentRecord["updatedAt"] = DateTimeOffset.UtcNow;

        await table.UpdateAsync(currentRecord);

        var result = new UpdateRecordOutput
        {
            RecordId = input.RecordId,
            Success = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            PreviousValues = previousValues
        };

        // Store idempotency record if key provided
        if (!string.IsNullOrEmpty(input.IdempotencyKey))
        {
            await _repository.SaveIdempotencyRecordAsync(input.IdempotencyKey, result);
        }

        _logger.LogInformation("Updated record {RecordId}.", input.RecordId);

        return result;
    }
}
