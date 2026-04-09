import styles from './WorkflowHistory.module.css';

interface WorkflowHistoryProps {
  instanceId: string;
}

export function WorkflowHistory({ instanceId }: WorkflowHistoryProps) {
  return (
    <div className={styles.container}>
      <h3 className={styles.title}>Execution History</h3>
      <div className={styles.empty}>
        {instanceId
          ? 'Execution history is not exposed by the current workflow runtime.'
          : 'Select a workflow to inspect execution history when the runtime supports it.'}
      </div>
    </div>
  );
}
