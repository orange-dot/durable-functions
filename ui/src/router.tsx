import { createRouter, createRootRoute, createRoute } from '@tanstack/react-router';
import { MainLayout } from './components/layout/MainLayout';
import { Dashboard } from './components/Dashboard';
import { LoginPage } from './components/LoginPage';
import { WorkflowList } from './components/workflows/WorkflowList';
import { WorkflowDetail } from './components/workflows/WorkflowDetail';
import { StartWorkflow } from './components/workflows/StartWorkflow';

// Root route
const rootRoute = createRootRoute({
  component: MainLayout,
});

// Index route (Dashboard)
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: Dashboard,
});

// Login route
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
});

// Workflows routes
const workflowsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/workflows',
  component: WorkflowList,
});

const workflowDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/workflows/$instanceId',
  component: WorkflowDetail,
});

const startWorkflowRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/workflows/new',
  component: StartWorkflow,
});

// Route tree
const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  workflowsRoute,
  workflowDetailRoute,
  startWorkflowRoute,
]);

// Create router
export const router = createRouter({ routeTree });

// Type declaration for router
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
