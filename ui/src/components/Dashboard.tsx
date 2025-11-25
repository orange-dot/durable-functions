import { Link } from '@tanstack/react-router';
import { useAuth } from '../auth/useAuth';

export function Dashboard() {
  const { user } = useAuth();

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
        <StatCard title="Total Workflows" value="--" color="blue" />
        <StatCard title="Running" value="--" color="yellow" />
        <StatCard title="Completed" value="--" color="green" />
        <StatCard title="Failed" value="--" color="red" />
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
          <Link
            to="/designer"
            className="px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 transition-colors"
          >
            Open Designer
          </Link>
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
        <h2 className="text-lg font-semibold text-gray-900 mb-4">
          Recent Workflows
        </h2>
        <div className="text-gray-500 text-center py-8">
          No workflows yet. Start one to see activity here.
        </div>
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
