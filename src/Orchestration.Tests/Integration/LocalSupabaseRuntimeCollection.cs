namespace Orchestration.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalSupabaseRuntimeCollection : ICollectionFixture<LocalSupabaseRuntimeFixture>
{
    public const string Name = "LocalSupabaseRuntime";
}
