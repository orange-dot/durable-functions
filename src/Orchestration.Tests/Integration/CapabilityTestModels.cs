using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Orchestration.Tests.Integration;

[Table("workflow_events")]
public sealed class CapabilityWorkflowEventRecord : BaseModel
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
