import { useState, useCallback } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { workflowApi } from '../../api/workflows';
import styles from './BulkActions.module.css';

interface BulkActionsProps {
  selectedIds: string[];
  onClearSelection: () => void;
}

type BulkAction = 'terminate' | 'suspend' | 'resume' | 'purge';
type SupportedBulkAction = 'terminate';

export function BulkActions({ selectedIds, onClearSelection }: BulkActionsProps) {
  const [confirmAction, setConfirmAction] = useState<SupportedBulkAction | null>(null);
  const queryClient = useQueryClient();

  const terminateMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await Promise.all(ids.map(id => workflowApi.terminate(id)));
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workflows'] });
      onClearSelection();
    },
  });

  const handleAction = useCallback((action: BulkAction) => {
    if (action === 'terminate') {
      setConfirmAction(action);
    }
  }, []);

  const executeAction = useCallback(() => {
    if (!confirmAction) return;

    terminateMutation.mutate(selectedIds);
    setConfirmAction(null);
  }, [confirmAction, selectedIds, terminateMutation]);

  if (selectedIds.length === 0) return null;

  const isProcessing = terminateMutation.isPending;

  return (
    <>
      <div className={styles.container}>
        <div className={styles.info}>
          <span className={styles.count}>{selectedIds.length}</span>
          <span className={styles.label}>selected</span>
          <button onClick={onClearSelection} className={styles.clearButton}>
            Clear
          </button>
        </div>

        <div className={styles.actions}>
          <button
            onClick={() => handleAction('terminate')}
            className={`${styles.actionButton} ${styles.warning}`}
            disabled={isProcessing}
            title="Terminate selected workflows"
          >
            ⏹️ Terminate
          </button>
        </div>
      </div>

      {confirmAction && (
        <div className={styles.overlay} onClick={() => setConfirmAction(null)}>
          <div className={styles.modal} onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
            <h3 className={styles.modalTitle}>
              Terminate Workflows
            </h3>
            <p className={styles.modalMessage}>
              {`Are you sure you want to terminate ${selectedIds.length} workflow(s)? Running or pending workflows will be stopped.`}
            </p>
            <div className={styles.modalActions}>
              <button
                onClick={() => setConfirmAction(null)}
                className={styles.cancelButton}
              >
                Cancel
              </button>
              <button
                onClick={executeAction}
                className={`${styles.confirmButton} ${styles.warning}`}
                disabled={isProcessing}
              >
                {isProcessing ? 'Processing...' : 'Terminate'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

// Hook for managing workflow selection
export function useWorkflowSelection() {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const toggleSelection = useCallback((id: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const selectAll = useCallback((ids: string[]) => {
    setSelectedIds(new Set(ids));
  }, []);

  const clearSelection = useCallback(() => {
    setSelectedIds(new Set());
  }, []);

  const isSelected = useCallback((id: string) => {
    return selectedIds.has(id);
  }, [selectedIds]);

  return {
    selectedIds: Array.from(selectedIds),
    toggleSelection,
    selectAll,
    clearSelection,
    isSelected,
    selectedCount: selectedIds.size,
  };
}
