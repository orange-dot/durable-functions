using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;
using Orchestration.Infrastructure.Storage;
using Orchestration.Supabase;

namespace Orchestration.Tests.Unit.Infrastructure;

public sealed class SupabaseServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSupabaseOrchestrationPersistence_registers_server_client_and_supabase_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupabaseOrchestrationPersistence(options =>
        {
            options.Url = "https://abc.supabase.co";
            options.AnonKey = "anon-key";
            options.ServiceRoleKey = "service-role-key";
            options.MapOnboardingRecordTable("Onboarding");
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SupabaseRuntimeOptions>>().Value;
        var statelessClient = provider.GetRequiredService<global::OrangeDot.Supabase.ISupabaseStatelessClient>();
        var clientFactory = provider.GetRequiredService<global::OrangeDot.Supabase.ISupabaseStatelessClientFactory>();
        var runtimeStore = provider.GetRequiredService<IWorkflowRuntimeStore>();
        var definitionStorage = provider.GetRequiredService<IWorkflowDefinitionStorage>();
        var scopeFactory = provider.GetRequiredService<IActivityCapabilityScopeFactory>();

        options.Url.Should().Be("https://abc.supabase.co");
        options.AnonKey.Should().Be("anon-key");
        options.ServiceRoleKey.Should().Be("service-role-key");
        statelessClient.Url.Should().Be("https://abc.supabase.co");
        statelessClient.AnonKey.Should().Be("anon-key");
        clientFactory.Should().NotBeNull();
        runtimeStore.Should().BeOfType<SupabaseWorkflowRuntimeStore>();
        definitionStorage.Should().BeOfType<SupabaseWorkflowDefinitionStorage>();
        scopeFactory.Should().BeOfType<SupabaseActivityCapabilityScopeFactory>();
    }
}
