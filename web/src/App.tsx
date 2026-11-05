import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { router } from './routes'

const queryClient = new QueryClient({
  defaultOptions: {
    // Don't retry client errors (4xx); they won't fix themselves.
    queries: {
      retry: (failureCount, error) =>
        failureCount < 2 && !('status' in error && Number(error.status) < 500),
    },
  },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
