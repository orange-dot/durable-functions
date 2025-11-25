import { Link } from '@tanstack/react-router';
import { useAuth } from '../auth/useAuth';
import { useWorkflows } from '../hooks/useWorkflows';
import { StatusBadge } from './common/StatusBadge';
import { LoadingSpinner } from './common/LoadingSpinner';

export function Dashboard() {
  const { user } = useAuth();
  const { data, isLoading } = useWorkflows();

  // Calculate stats from workflows
  const stats = {
    total: data?.workflows?.length ?? 0,
    running: data?.workflows?.filter(w => w.Status === 'Running').length ?? 0,
    completed: data?.workflows?.filter(w => w.Status === 'Completed').length ?? 0,
    failed: data?.workflows?.filter(w => w.Status === 'Failed').length ?? 0,
  };

  const recentWorkflows = data?.workflows?.slice(0, 5) ?? [];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <p className="text-gray-600">
          Welcome back{user ? `, ${user.name}` : ''}!
        </p>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <StatCard title="Total Workflows" value={isLoading ? '...' : stats.total} color="blue" />
        <StatCard title="Running" value={isLoading ? '...' : stats.running} color="yellow" />
        <StatCard title="Completed" value={isLoading ? '...' : stats.completed} color="green" />
        <StatCard title="Failed" value={isLoading ? '...' : stats.failed} color="red" />
      </div>

      {/* Quick Actions */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h2 className="text-lg font-semibold text-gray-900 mb-4">
          Quick Actions
        </h2>
        <div className="flex gap-4">
          <Link
            to="/workflows/new"
            className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
          >
            Start New Workflow
          </Link>
          <button
            disabled
            className="px-4 py-2 border border-gray-300 text-gray-400 rounded-lg cursor-not-allowed"
            title="Coming soon"
          >
            Open Designer
          </button>
          <Link
            to="/workflows"
            className="px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 transition-colors"
          >
            View All Workflows
          </Link>
        </div>
      </div>

      {/* Recent Workflows */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <div className="flex justify-between items-center mb-4">
          <h2 className="text-lg font-semibold text-gray-900">
            Recent Workflows
          </h2>
          <Link to="/workflows" className="text-sm text-blue-600 hover:underline">
            View all
          </Link>
        </div>

        {isLoading ? (
          <div className="flex justify-center py-8">
            <LoadingSpinner />
          </div>
        ) : recentWorkflows.length === 0 ? (
          <div className="text-gray-500 text-center py-8">
            No workflows yet. Start one to see activity here.
          </div>
        ) : (
          <div className="space-y-2">
            {recentWorkflows.map((workflow) => (
              <Link
                key={workflow.InstanceId}
                to="/workflows/$instanceId"
                params={{ instanceId: workflow.InstanceId }}
                className="block p-3 rounded-lg hover:bg-gray-50 transition-colors"
              >
                <div className="flex items-center justify-between">
                  <div>
                    <div className="font-mono text-sm text-gray-900">
                      {workflow.InstanceId.substring(0, 20)}...
                    </div>
                    <div className="text-xs text-gray-500">
                      {workflow.WorkflowType} • {new Date(workflow.CreatedAt).toLocaleString()}
                    </div>
                  </div>
                  <StatusBadge status={workflow.Status} />
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

interface StatCardProps {
  title: string;
  value: string | number;
  color: 'blue' | 'green' | 'yellow' | 'red';
}

function StatCard({ title, value, color }: StatCardProps) {
  const colorClasses = {
    blue: 'bg-blue-50 text-blue-600',
    green: 'bg-green-50 text-green-600',
    yellow: 'bg-yellow-50 text-yellow-600',
    red: 'bg-red-50 text-red-600',
  };

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4">
      <div className="text-sm text-gray-500">{title}</div>
      <div className={`text-2xl font-bold mt-1 ${colorClasses[color]}`}>
        {value}
      </div>
    </div>
  );
}
