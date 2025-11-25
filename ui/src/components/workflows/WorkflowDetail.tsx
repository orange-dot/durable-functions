import { useParams, Link } from '@tanstack/react-router';
import { useWorkflowDetail } from '../../hooks/useWorkflows';
import { StatusBadge } from '../common/StatusBadge';
import { JsonViewer } from '../common/JsonViewer';
import { LoadingSpinner } from '../common/LoadingSpinner';

export function WorkflowDetail() {
  const { instanceId } = useParams({ from: '/workflows/$instanceId' });
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
      <div className="bg-red-50 text-red-600 p-4 rounded-lg">
        Error loading workflow: {error.message}
      </div>
    );
  }

  if (!data) {
    return (
      <div className="bg-yellow-50 text-yellow-600 p-4 rounded-lg">
        Workflow not found
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Link
          to="/workflows"
          className="text-gray-500 hover:text-gray-700"
        >
          ← Back to list
        </Link>
      </div>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 font-mono">
            {data.instanceId}
          </h1>
          <p className="text-gray-600">{data.workflowType}</p>
        </div>
        <StatusBadge status={data.status} size="lg" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Info Card */}
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Details</h2>
          <dl className="space-y-2">
            <div className="flex justify-between">
              <dt className="text-gray-500">Created</dt>
              <dd className="text-gray-900">
                {new Date(data.createdAt).toLocaleString()}
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">Last Updated</dt>
              <dd className="text-gray-900">
                {new Date(data.lastUpdatedAt).toLocaleString()}
              </dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-gray-500">Entity ID</dt>
              <dd className="text-gray-900 font-mono text-sm">
                {data.input?.entityId ?? 'N/A'}
              </dd>
            </div>
          </dl>
        </div>

        {/* Actions Card */}
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Actions</h2>
          <div className="space-y-2">
            {data.status === 'Running' && (
              <button className="w-full px-4 py-2 border border-red-300 text-red-600 rounded-lg hover:bg-red-50">
                Terminate Workflow
              </button>
            )}
            <button className="w-full px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50">
              Raise Event
            </button>
          </div>
        </div>
      </div>

      {/* Input */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Input</h2>
        <JsonViewer data={data.input} />
      </div>

      {/* Output */}
      {data.output && (
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Output</h2>
          <JsonViewer data={data.output} />
        </div>
      )}

      {/* Step Results */}
      {data.stepResults && Object.keys(data.stepResults).length > 0 && (
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Step Results</h2>
          <JsonViewer data={data.stepResults} />
        </div>
      )}
    </div>
  );
}
