import { createRouter, createRootRoute, createRoute, redirect } from '@tanstack/react-router';
import { MainLayout } from './components/layout/MainLayout';
import { Dashboard } from './components/Dashboard';
import { LoginPage } from './components/LoginPage';
import { WorkflowList } from './components/workflows/WorkflowList';
import { WorkflowDetail } from './components/workflows/WorkflowDetail';
import { StartWorkflow } from './components/workflows/StartWorkflow';
import { DefinitionList } from './components/definitions/DefinitionList';
import { DefinitionDetail } from './components/definitions/DefinitionDetail';
import { ActivityList } from './components/activities/ActivityList';
import { WorkflowDesigner } from './components/designer/WorkflowDesigner';
import { PlaygroundPage } from './components/playground/PlaygroundPage';

// Auth check helper
function isAuthenticated() {
  const user = sessionStorage.getItem('demo-user');
  return !!user;
}

// Root route - no layout, just outlet
const rootRoute = createRootRoute();

// Login route - standalone page
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
  beforeLoad: () => {
    if (isAuthenticated()) {
      throw redirect({ to: '/' });
    }
  },
});

// Protected layout route
const protectedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'protected',
  component: MainLayout,
  beforeLoad: () => {
    if (!isAuthenticated()) {
      throw redirect({ to: '/login' });
    }
  },
});

// Dashboard - index route under protected
const indexRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/',
  component: Dashboard,
});

// Workflows routes
const workflowsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/workflows',
  component: WorkflowList,
});

const startWorkflowRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/workflows/new',
  component: StartWorkflow,
});

const workflowDetailRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/workflows/$instanceId',
  component: WorkflowDetail,
});

// Definitions routes
const definitionsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/definitions',
  component: DefinitionList,
});

const definitionDetailRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/definitions/$definitionId',
  component: DefinitionDetail,
});

// Activities route
const activitiesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/activities',
  component: ActivityList,
});

// Designer route
const designerRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/designer',
  component: WorkflowDesigner,
});

// Playground route
const playgroundRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/playground',
  component: PlaygroundPage,
});

// Route tree
const routeTree = rootRoute.addChildren([
  loginRoute,
  protectedRoute.addChildren([
    indexRoute,
    workflowsRoute,
    startWorkflowRoute,
    workflowDetailRoute,
    definitionsRoute,
    definitionDetailRoute,
    activitiesRoute,
    designerRoute,
    playgroundRoute,
  ]),
]);

// Create router
export const router = createRouter({ routeTree });

// Type declaration for router
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
