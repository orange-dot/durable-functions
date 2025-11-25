import { useNavigate } from '@tanstack/react-router';
import { useAuth } from '../auth/useAuth';
import { DEMO_USERS } from '../auth/DemoAuthProvider';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();

  // Note: Redirect is now handled by router beforeLoad

  const handleLogin = (userId: string) => {
    login(userId);
    navigate({ to: '/' });
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-100">
      <div className="bg-white p-8 rounded-xl shadow-lg max-w-md w-full">
        <div className="text-center mb-8">
          <h1 className="text-2xl font-bold text-gray-900">
            Orchestration Studio
          </h1>
          <p className="text-gray-600 mt-2">
            Azure Durable Functions Demo
          </p>
        </div>

        <div className="space-y-4">
          <p className="text-sm text-gray-500 text-center">
            Select a demo user to continue:
          </p>

          {DEMO_USERS.map((user) => (
            <button
              key={user.id}
              onClick={() => handleLogin(user.id)}
              className="w-full flex items-center gap-4 p-4 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
            >
              <div className="w-10 h-10 bg-blue-100 rounded-full flex items-center justify-center">
                <span className="text-blue-600 font-semibold">
                  {user.name.charAt(0)}
                </span>
              </div>
              <div className="text-left">
                <div className="font-medium text-gray-900">{user.name}</div>
                <div className="text-sm text-gray-500">
                  {user.email} • <span className="capitalize">{user.role}</span>
                </div>
              </div>
            </button>
          ))}
        </div>

        <div className="mt-8 p-4 bg-blue-50 rounded-lg">
          <p className="text-xs text-blue-800">
            <strong>Note:</strong> This is a demo authentication system.
            In production, this would integrate with Azure AD / Entra ID.
          </p>
        </div>
      </div>
    </div>
  );
}
