import { apiClient } from './client';
import type {
  WorkflowListResponse,
  WorkflowDetail,
  StartWorkflowRequest,
  StartWorkflowResponse,
} from './types';

export async function fetchWorkflows(): Promise<WorkflowListResponse> {
  const response = await apiClient.get<WorkflowListResponse>('/workflows');
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
