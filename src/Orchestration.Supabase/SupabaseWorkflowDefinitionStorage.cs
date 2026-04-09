using Microsoft.Extensions.Logging;
using Orchestration.Infrastructure.Storage;
using Orchestration.Supabase.Internal;
using Orchestration.Supabase.Models;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace Orchestration.Supabase;

/// <summary>
/// Supabase-backed workflow definition storage.
/// </summary>
public sealed class SupabaseWorkflowDefinitionStorage : IWorkflowDefinitionStorage
{
    private readonly global::OrangeDot.Supabase.ISupabaseStatelessClient _client;
    private readonly ILogger<SupabaseWorkflowDefinitionStorage> _logger;

    public SupabaseWorkflowDefinitionStorage(
        global::OrangeDot.Supabase.ISupabaseStatelessClient client,
        ILogger<SupabaseWorkflowDefinitionStorage> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Orchestration.Core.Workflow.WorkflowDefinition> GetAsync(string workflowType, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        var query = _client.Postgrest.Table<WorkflowDefinitionRow>()
            .Filter("workflow_type", Operator.Equals, workflowType);

        if (string.IsNullOrWhiteSpace(version))
        {
            query = query.Filter("is_latest", Operator.Equals, "true");
        }
        else
        {
            query = query.Filter("version", Operator.Equals, version);
        }

        var row = await query.Single().ConfigureAwait(false);

        if (row is null)
        {
            throw new InvalidOperationException(
                $"Workflow definition '{workflowType}'{(version is null ? string.Empty : $" version '{version}'")} not found.");
        }

        return SupabaseJson.DeserializeDefinition(row.DefinitionJson);
    }

    public async Task SaveAsync(Orchestration.Core.Workflow.WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var definitionId = WorkflowDefinitionIdentity.Create(definition.Id, definition.Version);

        await _client.Postgrest.Rpc<string>(
            "save_workflow_definition",
            new
            {
                p_definition_id = definitionId,
                p_workflow_type = definition.Id,
                p_version = definition.Version,
                p_definition_json = SupabaseJson.SerializeDefinition(definition)
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "Saved Supabase workflow definition {WorkflowType} version {Version} as {DefinitionId}.",
            definition.Id,
            definition.Version,
            definitionId);
    }

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string workflowType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        var response = await _client.Postgrest.Table<WorkflowDefinitionRow>()
            .Filter("workflow_type", Operator.Equals, workflowType)
            .Order("updated_at", Ordering.Descending)
            .Get()
            .ConfigureAwait(false);

        return response.Models
            .Select(model => model.Version)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListWorkflowTypesAsync()
    {
        var response = await _client.Postgrest.Table<WorkflowDefinitionRow>()
            .Filter("is_latest", Operator.Equals, "true")
            .Order("workflow_type", Ordering.Ascending)
            .Get()
            .ConfigureAwait(false);

        return response.Models
            .Select(model => model.WorkflowType)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public async Task DeleteAsync(string workflowType, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        await _client.Postgrest.Rpc<bool>(
            "delete_workflow_definition",
            new
            {
                p_workflow_type = workflowType,
                p_version = version
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "Deleted Supabase workflow definition {WorkflowType} version {Version}.",
            workflowType,
            version);
    }
}
