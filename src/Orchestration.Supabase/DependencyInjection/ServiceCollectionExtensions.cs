using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;
using Orchestration.Infrastructure.Storage;
using Orchestration.Supabase.Internal;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSupabaseOrchestrationPersistence(
        this IServiceCollection services,
        Action<Orchestration.Supabase.SupabaseRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddSupabaseOrchestrationPersistence((_, options) => configure(options));
    }

    public static IServiceCollection AddSupabaseOrchestrationPersistence(
        this IServiceCollection services,
        Action<IServiceProvider, Orchestration.Supabase.SupabaseRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton<IOptions<Orchestration.Supabase.SupabaseRuntimeOptions>>(serviceProvider =>
        {
            var options = new Orchestration.Supabase.SupabaseRuntimeOptions();
            configure(serviceProvider, options);
            return Microsoft.Extensions.Options.Options.Create(options);
        });

        services.TryAddSingleton<global::OrangeDot.Supabase.ISupabaseClient>(serviceProvider =>
        {
            var runtimeOptions = serviceProvider.GetRequiredService<IOptions<Orchestration.Supabase.SupabaseRuntimeOptions>>().Value;
            return SupabaseClientFactory.CreateInitializedClient(runtimeOptions);
        });

        services.TryAddSingleton<Orchestration.Supabase.SupabaseCapabilityFactory>();
        services.TryAddSingleton<IActivityCapabilityScopeFactory, Orchestration.Supabase.SupabaseActivityCapabilityScopeFactory>();
        services.TryAddSingleton<IWorkflowRuntimeStore, Orchestration.Supabase.SupabaseWorkflowRuntimeStore>();
        services.TryAddSingleton<IWorkflowDefinitionStorage, Orchestration.Supabase.SupabaseWorkflowDefinitionStorage>();

        return services;
    }
}
