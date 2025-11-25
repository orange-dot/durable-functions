export interface WorkflowListItem {
  InstanceId: string;
  Status: string;
  WorkflowType: string;
  CreatedAt: string;
  LastUpdatedAt: string;
}

export interface WorkflowListResponse {
  count: number;
  workflows: WorkflowListItem[];
}

export interface WorkflowInput {
  workflowType: string;
  entityId: string;
  idempotencyKey?: string;
  correlationId?: string;
  data?: Record<string, unknown>;
}

export interface WorkflowDetail {
  InstanceId: string;
  Status: string;
  WorkflowType: string;
  CreatedAt: string;
  LastUpdatedAt: string;
  Input?: unknown;
  Output?: unknown;
  CustomStatus?: unknown;
  FailureDetails?: string;
}

export interface StartWorkflowRequest {
  workflowType: string;
  entityId: string;
  idempotencyKey?: string;
  data?: Record<string, unknown>;
}

export interface StartWorkflowResponse {
  instanceId: string;
  statusQueryGetUri?: string;
}

export interface WorkflowDefinition {
  id: string;
  version: string;
  name: string;
  description?: string;
  startAt: string;
  states: Record<string, WorkflowState>;
  config?: {
    timeoutSeconds?: number;
    defaultRetryPolicy?: RetryPolicy;
  };
  metadata?: {
    author?: string;
    tags?: string[];
  };
}

export interface WorkflowState {
  type: 'Task' | 'Choice' | 'Wait' | 'Parallel' | 'Succeed' | 'Fail';
  comment?: string;
  next?: string;
  end?: boolean;
  activity?: string;
  input?: Record<string, string>;
  resultPath?: string;
  choices?: ChoiceRule[];
  default?: string;
  output?: Record<string, string>;
}

export interface ChoiceRule {
  condition: {
    operator: string;
    variable: string;
    comparisonType: string;
    value: unknown;
  };
  next: string;
}

export interface RetryPolicy {
  maxAttempts: number;
  initialIntervalSeconds: number;
  backoffCoefficient: number;
}
