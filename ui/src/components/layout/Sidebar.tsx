import { Link, useLocation } from '@tanstack/react-router';

interface NavItem {
  label: string;
  path: string;
  icon: string;
}

const navItems: NavItem[] = [
  { label: 'Dashboard', path: '/', icon: '📊' },
  { label: 'Playground', path: '/playground', icon: '🎮' },
  { label: 'Workflows', path: '/workflows', icon: '⚡' },
  { label: 'Designer', path: '/designer', icon: '🎨' },
  { label: 'Definitions', path: '/definitions', icon: '📋' },
  { label: 'Activities', path: '/activities', icon: '🔧' },
];

export function Sidebar() {
  const location = useLocation();

  return (
    <aside className="w-64 bg-dark-card border-r border-dark-border min-h-full">
      <nav className="p-4">
        <ul className="space-y-1">
          {navItems.map((item) => {
            const isActive = location.pathname === item.path ||
              (item.path !== '/' && location.pathname.startsWith(item.path));

            return (
              <li key={item.path}>
                <Link
                  to={item.path}
                  className={`flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                    isActive
                      ? 'bg-blue-900/50 text-blue-400'
                      : 'text-gray-300 hover:bg-dark-hover'
                  }`}
                >
                  <span>{item.icon}</span>
                  <span>{item.label}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="absolute bottom-0 left-0 w-64 p-4 border-t border-dark-border">
        <div className="text-xs text-gray-500">
          <div>Azure Durable Functions</div>
          <div>Orchestration Demo v1.0</div>
        </div>
      </div>
    </aside>
  );
}
