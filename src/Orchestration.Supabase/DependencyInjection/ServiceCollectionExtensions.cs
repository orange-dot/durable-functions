using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Contracts;
using Orchestration.Infrastructure.Storage;

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

        services.AddSupabaseServer((serviceProvider, options) =>
        {
            var runtimeOptions = serviceProvider.GetRequiredService<IOptions<Orchestration.Supabase.SupabaseRuntimeOptions>>().Value;

            options.Url = runtimeOptions.Url;
            options.AnonKey = runtimeOptions.AnonKey;
            options.ServiceRoleKey = runtimeOptions.ServiceRoleKey;
        });

        services.TryAddSingleton<global::OrangeDot.Supabase.ISupabaseStatelessClient>(serviceProvider =>
            serviceProvider.GetRequiredService<global::OrangeDot.Supabase.ISupabaseStatelessClientFactory>().CreateService());

        services.TryAddSingleton<Orchestration.Supabase.SupabaseCapabilityFactory>();
        services.TryAddSingleton<IActivityCapabilityScopeFactory, Orchestration.Supabase.SupabaseActivityCapabilityScopeFactory>();
        services.TryAddSingleton<IWorkflowRuntimeStore, Orchestration.Supabase.SupabaseWorkflowRuntimeStore>();
        services.TryAddSingleton<IWorkflowDefinitionStorage, Orchestration.Supabase.SupabaseWorkflowDefinitionStorage>();

        return services;
    }
}
