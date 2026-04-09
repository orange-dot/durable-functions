import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { fetchWorkflows, fetchWorkflowDetail, startWorkflow, terminateWorkflow, raiseEvent } from '../api/workflows';
import type { StartWorkflowRequest } from '../api/types';

interface UseWorkflowsOptions {
  pageSize?: number;
  continuationToken?: string | null;
  refetchInterval?: number | false;
}

export function useWorkflows(options: UseWorkflowsOptions = {}) {
  return useQuery({
    queryKey: ['workflows', options.pageSize ?? null, options.continuationToken ?? null],
    queryFn: () =>
      fetchWorkflows({
        pageSize: options.pageSize,
        continuationToken: options.continuationToken,
      }),
    refetchInterval: options.refetchInterval ?? 10000,
  });
}

export function useWorkflowDetail(instanceId: string) {
  return useQuery({
    queryKey: ['workflow', instanceId],
    queryFn: () => fetchWorkflowDetail(instanceId),
    enabled: !!instanceId,
    refetchInterval: 5000, // Refetch every 5 seconds for running workflows
  });
}

export function useStartWorkflow() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: StartWorkflowRequest) => startWorkflow(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflows'] });
    },
  });
}

export function useTerminateWorkflow() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (instanceId: string) => terminateWorkflow(instanceId),
    onSuccess: (_data, instanceId) => {
      queryClient.invalidateQueries({ queryKey: ['workflows'] });
      queryClient.invalidateQueries({ queryKey: ['workflow', instanceId] });
    },
  });
}

export function useRaiseEvent() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ instanceId, eventName, eventData }: { instanceId: string; eventName: string; eventData: unknown }) =>
      raiseEvent(instanceId, eventName, eventData),
    onSuccess: (_data, { instanceId }) => {
      queryClient.invalidateQueries({ queryKey: ['workflow', instanceId] });
    },
  });
}
