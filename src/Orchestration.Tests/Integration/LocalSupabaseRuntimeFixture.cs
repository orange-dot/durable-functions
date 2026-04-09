using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orchestration.Core.Contracts;
using Orchestration.Infrastructure.Storage;
using Orchestration.Supabase;

namespace Orchestration.Tests.Integration;

public sealed class LocalSupabaseRuntimeFixture : IAsyncLifetime
{
    private const string SupabaseUrlEnvironmentVariable = "SUPABASE_URL";
    private const string SupabaseAnonKeyEnvironmentVariable = "SUPABASE_ANON_KEY";
    private const string SupabaseServiceRoleKeyEnvironmentVariable = "SUPABASE_SERVICE_ROLE_KEY";
    private const string SupabaseJwtSecretEnvironmentVariable = "SUPABASE_JWT_SECRET";
    private const string SupabaseDbConnectionStringEnvironmentVariable = "ORCHESTRATION_SUPABASE_DB_CONNECTION_STRING";
    private const string DefaultSupabaseUrl = "http://127.0.0.1:54321";
    private const string DefaultSupabaseAnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0";
    private const string DefaultSupabaseJwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";
    private const string DefaultDbConnectionString =
        "Host=127.0.0.1;Port=54322;Username=supabase_admin;Password=postgres;Database=postgres;Pooling=false;Timeout=5;Command Timeout=15;SSL Mode=Disable";
    private static readonly DateTimeOffset DefaultJwtExpiry = DateTimeOffset.FromUnixTimeSeconds(1983812996);
    private static readonly SemaphoreSlim SchemaInitializationGate = new(1, 1);
    private static int _schemaInitialized;

    private ServiceProvider? _services;

    public string SupabaseUrl { get; private set; } = string.Empty;

    public string AnonKey { get; private set; } = string.Empty;

    public string ServiceRoleKey { get; private set; } = string.Empty;

    public string DbConnectionString { get; private set; } = string.Empty;

    public IWorkflowRuntimeStore RuntimeStore =>
        _services?.GetRequiredService<IWorkflowRuntimeStore>()
        ?? throw new InvalidOperationException("Supabase runtime fixture has not been initialized.");

    public IWorkflowDefinitionStorage DefinitionStorage =>
        _services?.GetRequiredService<IWorkflowDefinitionStorage>()
        ?? throw new InvalidOperationException("Supabase runtime fixture has not been initialized.");

    public SupabaseCapabilityFactory CapabilityFactory =>
        _services?.GetRequiredService<SupabaseCapabilityFactory>()
        ?? throw new InvalidOperationException("Supabase runtime fixture has not been initialized.");

    public Orchestration.Core.Capabilities.IActivityCapabilityScopeFactory ActivityCapabilityScopeFactory =>
        _services?.GetRequiredService<Orchestration.Core.Capabilities.IActivityCapabilityScopeFactory>()
        ?? throw new InvalidOperationException("Supabase runtime fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        SupabaseUrl = (Environment.GetEnvironmentVariable(SupabaseUrlEnvironmentVariable) ?? DefaultSupabaseUrl).TrimEnd('/');
        AnonKey = Environment.GetEnvironmentVariable(SupabaseAnonKeyEnvironmentVariable) ?? DefaultSupabaseAnonKey;
        DbConnectionString = Environment.GetEnvironmentVariable(SupabaseDbConnectionStringEnvironmentVariable) ?? DefaultDbConnectionString;
        ServiceRoleKey = Environment.GetEnvironmentVariable(SupabaseServiceRoleKeyEnvironmentVariable)
            ?? CreateServiceRoleJwt(Environment.GetEnvironmentVariable(SupabaseJwtSecretEnvironmentVariable) ?? DefaultSupabaseJwtSecret);

        await EnsureDatabaseReachableAsync().ConfigureAwait(false);
        await EnsureSchemaAppliedAsync().ConfigureAwait(false);
        await ResetAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupabaseOrchestrationPersistence(options =>
        {
            options.Url = SupabaseUrl;
            options.AnonKey = AnonKey;
            options.ServiceRoleKey = ServiceRoleKey;
            options.MapOnboardingRecordTable("Onboarding");
            options.MapOnboardingRecordTable("OnboardingRecord");
            options.MapTable<CapabilityWorkflowEventRecord>("workflow-events");
            options.MapStorageBucket("artifacts", "artifacts");
            options.MapEdgeFunction("echo", "echo");
        });

        _services = services.BuildServiceProvider(validateScopes: true);
        _ = _services.GetRequiredService<global::OrangeDot.Supabase.ISupabaseStatelessClient>();

        await EnsureRestSchemaReadyAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_services is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _services?.Dispose();
    }

    public async Task ResetAsync()
    {
        const string truncateSql = """
            truncate table public.onboarding_records,
                           public.step_executions,
                           public.workflow_events,
                           public.workflow_instances,
                           public.workflow_definitions
            restart identity cascade;
            """;

        await using var connection = new NpgsqlConnection(DbConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(truncateSql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static string ComputeDefinitionId(string workflowType, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var namespaceId = new Guid("B7C5CE49-2B67-4A0F-A0C1-3F640CC5B252");
        var nameBytes = Encoding.UTF8.GetBytes($"{workflowType}:{version}");
        var namespaceBytes = namespaceId.ToByteArray();

        SwapByteOrder(namespaceBytes);

        using var sha1 = SHA1.Create();
        sha1.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
        sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);

        var hash = sha1.Hash ?? throw new InvalidOperationException("Failed to compute workflow definition hash.");
        var newGuid = new byte[16];
        Array.Copy(hash, 0, newGuid, 0, 16);

        newGuid[6] = (byte)((newGuid[6] & 0x0F) | 0x50);
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);

        SwapByteOrder(newGuid);
        return new Guid(newGuid).ToString();
    }

    private async Task EnsureDatabaseReachableAsync()
    {
        await using var connection = new NpgsqlConnection(DbConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync().ConfigureAwait(false);
    }

    private async Task ApplySchemaAsync()
    {
        var schemaPath = FindRepositoryFile("db", "supabase", "001_orchestration_runtime.sql");
        var sql = await File.ReadAllTextAsync(schemaPath).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(DbConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task EnsureSchemaAppliedAsync()
    {
        if (Volatile.Read(ref _schemaInitialized) == 1)
        {
            return;
        }

        await SchemaInitializationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Volatile.Read(ref _schemaInitialized) == 1)
            {
                return;
            }

            await ApplySchemaAsync().ConfigureAwait(false);
            Volatile.Write(ref _schemaInitialized, 1);
        }
        finally
        {
            SchemaInitializationGate.Release();
        }
    }

    private async Task EnsureRestSchemaReadyAsync()
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/rest/v1/workflow_definitions?select=id&limit=1");
                request.Headers.Add("apikey", AnonKey);
                request.Headers.Add("Authorization", $"Bearer {ServiceRoleKey}");

                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastError = new HttpRequestException(
                    $"REST schema not ready: {(int)response.StatusCode} {response.ReasonPhrase}",
                    null,
                    response.StatusCode);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Supabase REST schema cache did not expose orchestration tables in time.", lastError);
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathSegments]);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(pathSegments)}");
    }

    private static string CreateServiceRoleJwt(string jwtSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtSecret);

        const string headerJson = """{"alg":"HS256","typ":"JWT"}""";
        var payloadJson = JsonSerializer.Serialize(new
        {
            iss = "supabase-demo",
            role = "service_role",
            exp = DefaultJwtExpiry.ToUnixTimeSeconds()
        });

        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{encodedHeader}.{encodedPayload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtSecret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
