import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { DemoAuthProvider } from './auth/DemoAuthProvider';
import { router } from './router';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5000,
      retry: 1,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <DemoAuthProvider>
        <RouterProvider router={router} />
      </DemoAuthProvider>
    </QueryClientProvider>
  );
}

export default App;
