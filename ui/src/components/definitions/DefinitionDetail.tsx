import { useParams, Link } from '@tanstack/react-router';
import { useDefinitionDetail, useDefinitionVersions } from '../../hooks/useDefinitions';
import { JsonViewer } from '../common/JsonViewer';
import { LoadingSpinner } from '../common/LoadingSpinner';

const STATE_TYPE_COLORS: Record<string, string> = {
  Task: 'bg-blue-100 text-blue-700',
  Choice: 'bg-purple-100 text-purple-700',
  Wait: 'bg-yellow-100 text-yellow-700',
  Parallel: 'bg-indigo-100 text-indigo-700',
  Succeed: 'bg-green-100 text-green-700',
  Fail: 'bg-red-100 text-red-700',
  Compensation: 'bg-orange-100 text-orange-700',
};

export function DefinitionDetail() {
  const { definitionId } = useParams({ strict: false }) as { definitionId: string };
  const { data, isLoading, error } = useDefinitionDetail(definitionId);
  const { data: versionsData } = useDefinitionVersions(definitionId);

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
        Error loading definition: {error.message}
      </div>
    );
  }

  if (!data) {
    return (
      <div className="bg-yellow-50 text-yellow-600 p-4 rounded-lg">
        Definition not found
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Link
          to="/definitions"
          className="text-gray-500 hover:text-gray-700"
        >
          &larr; Back to definitions
        </Link>
      </div>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{data.name}</h1>
          <p className="text-gray-600 font-mono">{data.id}</p>
        </div>
        <div className="flex items-center gap-2">
          <span className="px-3 py-1 bg-blue-100 text-blue-700 rounded-full text-sm">
            v{data.version}
          </span>
        </div>
      </div>

      {data.description && (
        <div className="bg-white rounded-lg border border-gray-200 p-4">
          <p className="text-gray-700">{data.description}</p>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Info Card */}
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Details</h2>
          <dl className="space-y-3">
            <div>
              <dt className="text-sm text-gray-500">Start State</dt>
              <dd className="text-gray-900 font-mono">{data.startAt}</dd>
            </div>
            <div>
              <dt className="text-sm text-gray-500">Total States</dt>
              <dd className="text-gray-900">{Object.keys(data.states || {}).length}</dd>
            </div>
            {data.config?.timeoutSeconds && (
              <div>
                <dt className="text-sm text-gray-500">Timeout</dt>
                <dd className="text-gray-900">{data.config.timeoutSeconds}s</dd>
              </div>
            )}
            {data.config?.compensationState && (
              <div>
                <dt className="text-sm text-gray-500">Compensation State</dt>
                <dd className="text-gray-900 font-mono">{data.config.compensationState}</dd>
              </div>
            )}
          </dl>
        </div>

        {/* Versions Card */}
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Versions</h2>
          {versionsData?.versions && versionsData.versions.length > 0 ? (
            <ul className="space-y-2">
              {versionsData.versions.map((version) => (
                <li key={version} className="flex items-center justify-between">
                  <span className="font-mono text-sm">{version}</span>
                  {version === data.version && (
                    <span className="text-xs text-green-600">current</span>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-gray-500 text-sm">No version history</p>
          )}
        </div>

        {/* Retry Policy Card */}
        {data.config?.defaultRetryPolicy && (
          <div className="bg-white rounded-lg border border-gray-200 p-6">
            <h2 className="text-lg font-semibold text-gray-900 mb-4">Default Retry Policy</h2>
            <dl className="space-y-2">
              <div className="flex justify-between">
                <dt className="text-gray-500">Max Attempts</dt>
                <dd className="text-gray-900">{data.config.defaultRetryPolicy.maxAttempts}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Initial Interval</dt>
                <dd className="text-gray-900">{data.config.defaultRetryPolicy.initialIntervalSeconds}s</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-gray-500">Backoff Coefficient</dt>
                <dd className="text-gray-900">{data.config.defaultRetryPolicy.backoffCoefficient}x</dd>
              </div>
            </dl>
          </div>
        )}
      </div>

      {/* States List */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">States</h2>
        <div className="space-y-4">
          {Object.entries(data.states || {}).map(([stateName, state]) => (
            <div
              key={stateName}
              className={`border rounded-lg p-4 ${
                stateName === data.startAt ? 'border-green-300 bg-green-50' : 'border-gray-200'
              }`}
            >
              <div className="flex items-center justify-between mb-2">
                <div className="flex items-center gap-2">
                  <h3 className="font-semibold text-gray-900 font-mono">{stateName}</h3>
                  {stateName === data.startAt && (
                    <span className="text-xs text-green-600">(start)</span>
                  )}
                </div>
                <span className={`px-2 py-1 rounded-full text-xs ${STATE_TYPE_COLORS[state.type || ''] || 'bg-gray-100 text-gray-700'}`}>
                  {state.type}
                </span>
              </div>

              {state.comment && (
                <p className="text-sm text-gray-600 mb-2">{state.comment}</p>
              )}

              <div className="flex flex-wrap gap-2 text-sm">
                {state.activity && (
                  <span className="px-2 py-1 bg-gray-100 text-gray-700 rounded">
                    Activity: {state.activity}
                  </span>
                )}
                {state.next && (
                  <span className="px-2 py-1 bg-gray-100 text-gray-700 rounded">
                    Next: {state.next}
                  </span>
                )}
                {state.default && (
                  <span className="px-2 py-1 bg-gray-100 text-gray-700 rounded">
                    Default: {state.default}
                  </span>
                )}
                {state.end && (
                  <span className="px-2 py-1 bg-green-100 text-green-700 rounded">
                    End State
                  </span>
                )}
                {state.externalEvent && (
                  <span className="px-2 py-1 bg-yellow-100 text-yellow-700 rounded">
                    Waits for: {state.externalEvent.eventName}
                  </span>
                )}
                {state.compensateWith && (
                  <span className="px-2 py-1 bg-orange-100 text-orange-700 rounded">
                    Compensate: {state.compensateWith}
                  </span>
                )}
              </div>

              {state.choices && state.choices.length > 0 && (
                <div className="mt-2 pl-4 border-l-2 border-purple-200">
                  <p className="text-xs text-gray-500 mb-1">Choices:</p>
                  {state.choices.map((choice, idx) => (
                    <div key={idx} className="text-sm text-gray-600">
                      if <code className="text-purple-600">{choice.condition.variable}</code>{' '}
                      {choice.condition.comparisonType} {JSON.stringify(choice.condition.value)}{' '}
                      &rarr; <span className="font-mono">{choice.next}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      </div>

      {/* Raw JSON */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">Raw Definition</h2>
        <JsonViewer data={data} />
      </div>
    </div>
  );
}
