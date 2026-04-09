using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestration.Core.Capabilities;
using Orchestration.Core.Workflow.Interpreter;
using Orchestration.Core.Contracts;
using Orchestration.Infrastructure.Data;
using Orchestration.Infrastructure.Data.Repositories;
using Orchestration.Infrastructure.Storage;
using Orchestration.Infrastructure.External;
using Orchestration.Functions.Capabilities;
using Orchestration.Functions.Activities.Registry;
using Orchestration.Supabase;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configure Durable Task JSON serialization to be case-insensitive
        // This ensures workflow definitions can use lowercase enum values like "log" instead of "Log"
        services.Configure<DurableTaskWorkerOptions>(options =>
        {
            options.DataConverter = new JsonDataConverter(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
            });
        });

        // Core services
        services.AddSingleton<IWorkflowInterpreter, WorkflowInterpreter>();
        services.AddSingleton<IJsonPathResolver, JsonPathResolver>();

        // Activity Registry
        services.AddSingleton<IActivityRegistry, ActivityRegistry>();

        // Database
        var sqlConnectionString = context.Configuration["SqlConnectionString"];
        if (!string.IsNullOrEmpty(sqlConnectionString))
        {
            services.AddDbContext<OrchestrationDbContext>(options =>
                options.UseSqlServer(sqlConnectionString));
        }
        else
        {
            // Use in-memory database for development
            services.AddDbContext<OrchestrationDbContext>(options =>
                options.UseInMemoryDatabase("OrchestrationDb"));
        }

        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        var supabaseUrl = context.Configuration["SUPABASE_URL"] ?? context.Configuration["Supabase:Url"];
        var supabaseAnonKey = context.Configuration["SUPABASE_ANON_KEY"] ?? context.Configuration["Supabase:AnonKey"];
        var supabaseServiceRoleKey = context.Configuration["SUPABASE_SERVICE_ROLE_KEY"] ?? context.Configuration["Supabase:ServiceRoleKey"];

        if (!string.IsNullOrWhiteSpace(supabaseUrl) &&
            !string.IsNullOrWhiteSpace(supabaseAnonKey) &&
            !string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
        {
            services.AddSupabaseOrchestrationPersistence(options =>
            {
                options.Url = supabaseUrl;
                options.AnonKey = supabaseAnonKey;
                options.ServiceRoleKey = supabaseServiceRoleKey;
                options.MapOnboardingRecordTable("Onboarding");
                options.MapOnboardingRecordTable("OnboardingRecord");
            });
        }
        else
        {
            services.AddSingleton<IWorkflowDefinitionStorage, WorkflowDefinitionStorage>();
            services.AddScoped<IActivityCapabilityScopeFactory, UnavailableActivityCapabilityScopeFactory>();
        }

        // HTTP clients
        services.AddHttpClient();
        services.AddHttpClient<IExternalApiClient, ExternalApiClient>();
    })
    .Build();

host.Run();
