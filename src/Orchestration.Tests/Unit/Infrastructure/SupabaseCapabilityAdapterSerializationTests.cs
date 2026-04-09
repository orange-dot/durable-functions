using FluentAssertions;
using Newtonsoft.Json;
using Orchestration.Supabase.Internal;
using Supabase.Postgrest;

namespace Orchestration.Tests.Unit.Infrastructure;

public sealed class SupabaseCapabilityAdapterSerializationTests
{
    [Fact]
    public void OnboardingRecord_insert_serialization_includes_primary_key()
    {
        var model = new SupabaseOnboardingRecordModel
        {
            Id = "record-123",
            EntityId = "entity-123",
            Status = "pending"
        };

        var resolver = new PostgrestContractResolver();
        resolver.SetState(isInsert: true);

        var json = JsonConvert.SerializeObject(
            model,
            new JsonSerializerSettings { ContractResolver = resolver });

        json.Should().Contain("\"id\":\"record-123\"");
        json.Should().Contain("\"entity_id\":\"entity-123\"");
        json.Should().Contain("\"status\":\"pending\"");
    }
}
