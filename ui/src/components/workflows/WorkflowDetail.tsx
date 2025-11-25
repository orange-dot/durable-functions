import { useParams, Link } from '@tanstack/react-router';
import { useWorkflowDetail } from '../../hooks/useWorkflows';
import { StatusBadge } from '../common/StatusBadge';
import { JsonViewer } from '../common/JsonViewer';
import { LoadingSpinner } from '../common/LoadingSpinner';

export function WorkflowDetail() {
  const { instanceId } = useParams({ strict: false }) as { instanceId: string };
  const { data, isLoading, error } = useWorkflowDetail(instanceId);

  if (isLoading) {
    return (
      <div className="flex justify-center py-12">
        <LoadingSpinner />
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-900/30 text-red-400 p-4 rounded-lg border border-red-800">
        Error loading workflow: {error.message}
      </div>
    );
  }

  if (!data) {
    return (
      <div className="bg-yellow-900/30 text-yellow-400 p-4 rounded-lg border border-yellow-800">
        Workflow not found
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Link
          to="/workflows"
          className="text-gray-400 hover:text-gray-200"
        >
          ← Back to list
        </Link>
      </div>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-100 font-mono">
            {data.InstanceId}
          </h1>
          <p className="text-gray-400">{data.WorkflowType}</p>
        </div>
        <StatusBadge status={data.Status} size="lg" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Info Card */}
        <div className="bg-dark-card rounded-lg border border-dark-border p-6">
          <h2 className="text-lg font-semibold text-gray-100 mb-4">Details</h2>
          <dl className="space-y-2">
            <div className="flex justify-between">
              <dt className="text-gray-400">Created</dt>
              <dd className="text-gray-200">
                {new Date(data.CreatedAt).toLocaleString()}
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-400">Last Updated</dt>
              <dd className="text-gray-200">
                {new Date(data.LastUpdatedAt).toLocaleString()}
              </dd>
            </div>
            {data.FailureDetails && (
              <div className="flex justify-between">
                <dt className="text-gray-400">Failure</dt>
                <dd className="text-red-400 text-sm">
                  {data.FailureDetails}
                </dd>
              </div>
            )}
          </dl>
        </div>

        {/* Actions Card */}
        <div className="bg-dark-card rounded-lg border border-dark-border p-6">
          <h2 className="text-lg font-semibold text-gray-100 mb-4">Actions</h2>
          <div className="space-y-2">
            {data.Status === 'Running' && (
              <button className="w-full px-4 py-2 border border-red-800 text-red-400 rounded-lg hover:bg-red-900/30">
                Terminate Workflow
              </button>
            )}
            <button className="w-full px-4 py-2 border border-dark-border text-gray-300 rounded-lg hover:bg-dark-hover">
              Raise Event
            </button>
          </div>
        </div>
      </div>

      {/* Input */}
      <div className="bg-dark-card rounded-lg border border-dark-border p-6">
        <h2 className="text-lg font-semibold text-gray-100 mb-4">Input</h2>
        <JsonViewer data={data.Input} />
      </div>

      {/* Output */}
      {data.Output !== undefined && data.Output !== null && (
        <div className="bg-dark-card rounded-lg border border-dark-border p-6">
          <h2 className="text-lg font-semibold text-gray-100 mb-4">Output</h2>
          <JsonViewer data={data.Output} />
        </div>
      )}

      {/* Custom Status */}
      {data.CustomStatus !== undefined && data.CustomStatus !== null && (
        <div className="bg-dark-card rounded-lg border border-dark-border p-6">
          <h2 className="text-lg font-semibold text-gray-100 mb-4">Custom Status</h2>
          <JsonViewer data={data.CustomStatus} />
        </div>
      )}
    </div>
  );
}
