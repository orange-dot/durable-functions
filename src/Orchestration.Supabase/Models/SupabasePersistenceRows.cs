using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Orchestration.Supabase.Models;

[Table("workflow_definitions")]
internal sealed class WorkflowDefinitionRow : BaseModel
{
    [PrimaryKey("id", true)]
    public string? Id { get; set; }

    [Column("workflow_type")]
    public string WorkflowType { get; set; } = null!;

    [Column("version")]
    public string Version { get; set; } = null!;

    [Column("definition_json")]
    public object? DefinitionJson { get; set; }

    [Column("is_latest")]
    public bool IsLatest { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset UpdatedAt { get; set; }
}

[Table("workflow_instances")]
internal sealed class WorkflowInstanceRow : BaseModel
{
    [PrimaryKey("instance_id", true)]
    public string? InstanceId { get; set; }

    [Column("definition_id")]
    public string DefinitionId { get; set; } = null!;

    [Column("definition_workflow_type")]
    public string DefinitionWorkflowType { get; set; } = null!;

    [Column("definition_version")]
    public string? DefinitionVersion { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("current_state_name")]
    public string? CurrentStateName { get; set; }

    [Column("runtime_state")]
    public object? RuntimeState { get; set; }

    [Column("lease_owner")]
    public string? LeaseOwner { get; set; }

    [Column("lease_expires_at")]
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    [Column("initiated_by_user_id")]
    public string? InitiatedByUserId { get; set; }

    [Column("requested_by_user_id")]
    public string? RequestedByUserId { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }
}

[Table("step_executions")]
internal sealed class WorkflowStepExecutionRow : BaseModel
{
    [PrimaryKey("step_execution_id", true)]
    public string? StepExecutionId { get; set; }

    [Column("instance_id")]
    public string InstanceId { get; set; } = null!;

    [Column("state_name")]
    public string StateName { get; set; } = null!;

    [Column("activity_name")]
    public string? ActivityName { get; set; }

    [Column("attempt")]
    public int Attempt { get; set; }

    [Column("is_compensation")]
    public bool IsCompensation { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("decision_json")]
    public object? DecisionJson { get; set; }

    [Column("outcome_json")]
    public object? OutcomeJson { get; set; }

    [Column("error_code")]
    public string? ErrorCode { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("finished_at")]
    public DateTimeOffset? FinishedAt { get; set; }
}

[Table("workflow_events")]
internal sealed class WorkflowEventRow : BaseModel
{
    [PrimaryKey("event_id", true)]
    public string? EventId { get; set; }

    [Column("instance_id")]
    public string InstanceId { get; set; } = null!;

    [Column("event_name")]
    public string EventName { get; set; } = null!;

    [Column("payload")]
    public object? Payload { get; set; }

    [Column("recorded_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTimeOffset RecordedAt { get; set; }

    [Column("consumed_at")]
    public DateTimeOffset? ConsumedAt { get; set; }

    [Column("consumed_by_state")]
    public string? ConsumedByState { get; set; }
}
