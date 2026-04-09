using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;
using Orchestration.Functions.Activities.Database;

namespace Orchestration.Tests.Integration;

[Collection(LocalSupabaseRuntimeCollection.Name)]
public sealed class SupabaseDatabaseActivityIntegrationTests(LocalSupabaseRuntimeFixture fixture)
{
    [LocalSupabaseFact]
    [Trait("Category", "Integration")]
    public async Task DatabaseActivities_round_trip_against_live_supabase()
    {
        await fixture.ResetAsync();

        var repository = new InMemoryIdempotencyRepository();
        var createActivity = new CreateRecordActivity(
            repository,
            fixture.ActivityCapabilityScopeFactory,
            NullLogger<CreateRecordActivity>.Instance);
        var getActivity = new GetRecordActivity(
            fixture.ActivityCapabilityScopeFactory,
            NullLogger<GetRecordActivity>.Instance);
        var updateActivity = new UpdateRecordActivity(
            repository,
            fixture.ActivityCapabilityScopeFactory,
            NullLogger<UpdateRecordActivity>.Instance);
        var compensateActivity = new CompensateCreateRecordActivity(
            repository,
            fixture.ActivityCapabilityScopeFactory,
            NullLogger<CompensateCreateRecordActivity>.Instance);

        var createResult = await createActivity.Run(new CreateRecordInput
        {
            RecordType = "Onboarding",
            IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            Data = new Dictionary<string, object?>
            {
                ["entityId"] = "device-42",
                ["status"] = "pending",
                ["payload"] = new Dictionary<string, object?>
                {
                    ["step"] = 1
                }
            },
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        var getResult = await getActivity.Run(new GetRecordInput
        {
            RecordId = createResult.RecordId,
            RecordType = "Onboarding",
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        });

        getResult.Found.Should().BeTrue();
        getResult.Data.Should().NotBeNull();
        getResult.Data!["entityId"].Should().Be("device-42");
        getResult.Data["status"].Should().Be("pending");

        var updateResult = await updateActivity.Run(new UpdateRecordInput
        {
            RecordId = createResult.RecordId,
            RecordType = "Onboarding",
            IdempotencyKey = $"update-{Guid.NewGuid():N}",
            Updates = new Dictionary<string, object?>
            {
                ["status"] = "complete",
                ["payload"] = new Dictionary<string, object?>
                {
                    ["step"] = 2
                }
            },
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        updateResult.Success.Should().BeTrue();
        updateResult.PreviousValues.Should().NotBeNull();
        updateResult.PreviousValues!["status"].Should().Be("pending");

        var updatedResult = await getActivity.Run(new GetRecordInput
        {
            RecordId = createResult.RecordId,
            RecordType = "Onboarding",
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        });

        updatedResult.Found.Should().BeTrue();
        updatedResult.Data.Should().NotBeNull();
        updatedResult.Data!["status"].Should().Be("complete");

        var compensateResult = await compensateActivity.Run(new CompensateCreateRecordInput
        {
            RecordId = createResult.RecordId,
            RecordType = "Onboarding",
            IdempotencyKey = createResult.RecordId,
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.ReadWrite)
            ]
        });

        compensateResult.Success.Should().BeTrue();
        compensateResult.WasAlreadyDeleted.Should().BeFalse();

        var deletedResult = await getActivity.Run(new GetRecordInput
        {
            RecordId = createResult.RecordId,
            RecordType = "Onboarding",
            CapabilityGrants =
            [
                new CapabilityGrant("Onboarding", CapabilityKind.Table, CapabilityAccess.Read)
            ]
        });

        deletedResult.Found.Should().BeFalse();

        repository.LegacyCrudCallCount.Should().Be(0);
    }

    private sealed class InMemoryIdempotencyRepository : IWorkflowRepository
    {
        private readonly Dictionary<string, object> _idempotencyResults = new(StringComparer.Ordinal);

        public int LegacyCrudCallCount { get; private set; }

        public Task SaveIdempotencyRecordAsync(string key, object result, CancellationToken cancellationToken = default)
        {
            _idempotencyResults[key] = result;
            return Task.CompletedTask;
        }

        public Task<T?> GetIdempotencyRecordAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_idempotencyResults.TryGetValue(key, out var value) && value is T typedValue)
            {
                return Task.FromResult<T?>(typedValue);
            }

            return Task.FromResult<T?>(default);
        }

        public Task<bool> IdempotencyRecordExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_idempotencyResults.ContainsKey(key));
        }

        public Task<T> CreateRecordAsync<T>(T record, string idempotencyKey, CancellationToken cancellationToken = default)
            where T : class
        {
            LegacyCrudCallCount++;
            throw new InvalidOperationException("Legacy repository CRUD path should not be used by migrated database activities.");
        }

        public Task<T> UpdateRecordAsync<T>(T record, CancellationToken cancellationToken = default)
            where T : class
        {
            LegacyCrudCallCount++;
            throw new InvalidOperationException("Legacy repository CRUD path should not be used by migrated database activities.");
        }

        public Task<T?> GetRecordAsync<T>(string id, CancellationToken cancellationToken = default)
            where T : class
        {
            LegacyCrudCallCount++;
            throw new InvalidOperationException("Legacy repository CRUD path should not be used by migrated database activities.");
        }

        public Task DeleteRecordAsync<T>(string id, CancellationToken cancellationToken = default)
            where T : class
        {
            LegacyCrudCallCount++;
            throw new InvalidOperationException("Legacy repository CRUD path should not be used by migrated database activities.");
        }
    }
}
