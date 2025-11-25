import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { fetchWorkflows, fetchWorkflowDetail, startWorkflow } from '../api/workflows';
import type { StartWorkflowRequest } from '../api/types';

export function useWorkflows() {
  return useQuery({
    queryKey: ['workflows'],
    queryFn: fetchWorkflows,
    refetchInterval: 10000, // Refetch every 10 seconds
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
