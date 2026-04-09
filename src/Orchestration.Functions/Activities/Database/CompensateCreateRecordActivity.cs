using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;

namespace Orchestration.Functions.Activities.Database;

/// <summary>
/// Input for compensating a record creation.
/// </summary>
public sealed class CompensateCreateRecordInput
{
    public required string RecordId { get; init; }
    public required string RecordType { get; init; }
    public string? IdempotencyKey { get; init; }
    public IReadOnlyList<CapabilityGrant>? CapabilityGrants { get; init; }
}

/// <summary>
/// Output from compensating a record creation.
/// </summary>
public sealed class CompensateCreateRecordOutput
{
    public required string RecordId { get; init; }
    public bool Success { get; init; }
    public bool WasAlreadyDeleted { get; init; }
    public DateTimeOffset CompensatedAt { get; init; }
}

/// <summary>
/// Activity that rolls back a record creation by deleting the record.
/// </summary>
public class CompensateCreateRecordActivity
{
    private readonly IWorkflowRepository _repository;
    private readonly IActivityCapabilityScopeFactory _scopeFactory;
    private readonly ILogger<CompensateCreateRecordActivity> _logger;

    public CompensateCreateRecordActivity(
        IWorkflowRepository repository,
        IActivityCapabilityScopeFactory scopeFactory,
        ILogger<CompensateCreateRecordActivity> logger)
    {
        _repository = repository;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Function(nameof(CompensateCreateRecordActivity))]
    public async Task<CompensateCreateRecordOutput> Run([ActivityTrigger] CompensateCreateRecordInput input)
    {
        _logger.LogInformation(
            "Compensating creation of record {RecordId} of type {RecordType}.",
            input.RecordId, input.RecordType);

        // Check idempotency
        var idempotencyKey = $"compensate-{input.IdempotencyKey ?? input.RecordId}";
        var existing = await _repository.GetIdempotencyRecordAsync<CompensateCreateRecordOutput>(idempotencyKey);
        if (existing != null)
        {
            _logger.LogInformation(
                "Found existing compensation for record {RecordId}.",
                input.RecordId);
            return existing;
        }

        // Check if record still exists
        var table = DatabaseActivityCapabilityResolver.ResolveReadWriteTable(
            _scopeFactory,
            input.RecordType,
            input.CapabilityGrants);

        var record = await table.GetByIdAsync(input.RecordId);
        if (record == null)
        {
            var notFoundResult = new CompensateCreateRecordOutput
            {
                RecordId = input.RecordId,
                Success = true,
                WasAlreadyDeleted = true,
                CompensatedAt = DateTimeOffset.UtcNow
            };
            await _repository.SaveIdempotencyRecordAsync(idempotencyKey, notFoundResult);
            return notFoundResult;
        }

        // Delete the record
        await table.DeleteByIdAsync(input.RecordId);

        var result = new CompensateCreateRecordOutput
        {
            RecordId = input.RecordId,
            Success = true,
            WasAlreadyDeleted = false,
            CompensatedAt = DateTimeOffset.UtcNow
        };

        await _repository.SaveIdempotencyRecordAsync(idempotencyKey, result);

        _logger.LogInformation(
            "Compensated creation of record {RecordId}.",
            input.RecordId);

        return result;
    }
}
