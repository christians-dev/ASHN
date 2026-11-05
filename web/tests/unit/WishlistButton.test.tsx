import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { WishlistButton } from '../../src/features/wishlist/WishlistButton'
import { sessionQueryKey } from '../../src/shared/auth/useSession'

describe('WishlistButton', () => {
  it('sends a visitor to sign in and back to the product afterwards', async () => {
    const router = createMemoryRouter(
      [
        {
          path: '/products/:slug',
          element: <WishlistButton productId="p1" productName="Black Hoodie" />,
        },
        { path: '/sign-in', element: <p>Sign-in page</p> },
      ],
      { initialEntries: ['/products/black-hoodie'] },
    )
    // A known signed-out session, so nothing is fetched.
    const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity } } })
    queryClient.setQueryData(sessionQueryKey, { isAuthenticated: false, roles: [] })
    render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Save Black Hoodie to wishlist' }))

    expect(await screen.findByText('Sign-in page')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?returnUrl=%2Fproducts%2Fblack-hoodie')
  })
})
