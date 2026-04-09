import { apiClient } from './client';
import type {
  WorkflowListResponse,
  WorkflowDetail,
  StartWorkflowRequest,
  StartWorkflowResponse,
} from './types';

export interface FetchWorkflowsOptions {
  pageSize?: number;
  continuationToken?: string | null;
}

export async function fetchWorkflows(options: FetchWorkflowsOptions = {}): Promise<WorkflowListResponse> {
  const params: Record<string, string | number> = {};

  if (options.pageSize !== undefined) {
    params.pageSize = options.pageSize;
  }

  if (options.continuationToken) {
    params.continuationToken = options.continuationToken;
  }

  const response = await apiClient.get<WorkflowListResponse>('/workflows', { params });
  return response.data;
}

export async function fetchWorkflowDetail(instanceId: string): Promise<WorkflowDetail> {
  const response = await apiClient.get<WorkflowDetail>(`/workflows/${instanceId}`);
  return response.data;
}

export async function startWorkflow(request: StartWorkflowRequest): Promise<StartWorkflowResponse> {
  const response = await apiClient.post<StartWorkflowResponse>('/workflows', request);
  return response.data;
}

export async function raiseEvent(
  instanceId: string,
  eventName: string,
  eventData: unknown
): Promise<void> {
  await apiClient.post(`/workflows/${instanceId}/events/${eventName}`, eventData);
}

export async function terminateWorkflow(instanceId: string): Promise<void> {
  await apiClient.post(`/workflows/${instanceId}/terminate`);
}

// Convenience object for named imports
export const workflowApi = {
  listInstances: fetchWorkflows,
  getInstance: fetchWorkflowDetail,
  start: startWorkflow,
  raiseEvent,
  terminate: terminateWorkflow,
};
