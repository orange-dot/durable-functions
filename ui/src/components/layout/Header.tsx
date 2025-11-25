import { Link } from '@tanstack/react-router';
import { useAuth } from '../../auth/useAuth';

export function Header() {
  const { user, logout, isAuthenticated } = useAuth();

  return (
    <header className="bg-white border-b border-gray-200 px-6 py-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Link to="/" className="text-xl font-bold text-gray-900">
            Orchestration Studio
          </Link>
          <span className="text-xs bg-blue-100 text-blue-800 px-2 py-1 rounded">
            Demo
          </span>
        </div>

        <div className="flex items-center gap-4">
          {isAuthenticated && user && (
            <>
              <div className="text-sm text-gray-600">
                <span className="font-medium">{user.name}</span>
                <span className="mx-2">•</span>
                <span className="capitalize">{user.role}</span>
              </div>
              <button
                onClick={logout}
                className="text-sm text-gray-500 hover:text-gray-700"
              >
                Logout
              </button>
            </>
          )}
        </div>
      </div>
    </header>
  );
}
