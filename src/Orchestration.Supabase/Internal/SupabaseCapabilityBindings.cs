namespace Orchestration.Supabase.Internal;

internal sealed record SupabaseTableCapabilityBinding(string ResourceName, Type RecordType);

internal sealed record SupabaseStorageBucketCapabilityBinding(string ResourceName, string BucketName);

internal sealed record SupabaseEdgeFunctionCapabilityBinding(string ResourceName, string FunctionName);
