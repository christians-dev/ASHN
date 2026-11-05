import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RegisterPage } from '../../src/features/account/RegisterPage'
import { SignInPage } from '../../src/features/account/SignInPage'

function renderAt(path: string, element: React.ReactNode) {
  const router = createMemoryRouter(
    [
      { path: '/sign-in', element },
      { path: '/register', element },
    ],
    { initialEntries: [path] },
  )
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Answers every API call with the given problem response, the way the server would. */
function serverRespondsWith(status: number, problem: object) {
  const fetch = vi.fn(
    async () =>
      new Response(JSON.stringify(problem), {
        status,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
  )
  vi.stubGlobal('fetch', fetch)
  return fetch
}

afterEach(() => vi.unstubAllGlobals())

describe('RegisterPage', () => {
  it('checks required fields and password length before calling the server', async () => {
    const fetch = serverRespondsWith(500, {})
    renderAt('/register', <RegisterPage />)

    await userEvent.type(screen.getByLabelText('Password'), 'short')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(screen.getByText('Enter your name.')).toBeInTheDocument()
    expect(screen.getByText('Enter your email address.')).toBeInTheDocument()
    expect(screen.getByText('Use at least 8 characters.')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true')
    expect(fetch).not.toHaveBeenCalled()
  })

  it('shows server validation errors next to the right field', async () => {
    serverRespondsWith(400, {
      status: 400,
      errors: {
        email: ['Email is already taken.'],
        password: ['This password appears in lists of breached passwords.'],
      },
    })
    renderAt('/register', <RegisterPage />)

    await userEvent.type(screen.getByLabelText('Name'), 'Sam')
    await userEvent.type(screen.getByLabelText('Email'), 'taken@test.local')
    await userEvent.type(screen.getByLabelText('Password'), 'password123')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByText('Email is already taken.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Email is already taken.')
    expect(screen.getByText(/breached passwords/)).toBeInTheDocument()
  })
})

describe('SignInPage', () => {
  it('requires email and password', async () => {
    const fetch = serverRespondsWith(500, {})
    renderAt('/sign-in', <SignInPage />)

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(screen.getByText('Enter your email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('shows the lockout message from the server', async () => {
    serverRespondsWith(423, {
      status: 423,
      detail: 'Too many failed attempts. Please try again in 15 minutes.',
    })
    renderAt('/sign-in', <SignInPage />)

    await userEvent.type(screen.getByLabelText('Email'), 'sam@test.local')
    await userEvent.type(screen.getByLabelText('Password'), 'whatever-it-is')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('try again in 15 minutes')
  })

  it('sends Google sign-in back to the page the shopper came from', () => {
    serverRespondsWith(500, {})
    renderAt('/sign-in?returnUrl=%2Fcheckout', <SignInPage />)

    expect(screen.getByRole('link', { name: 'Continue with Google' })).toHaveAttribute(
      'href',
      '/api/auth/google?returnUrl=%2Fcheckout',
    )
  })

  it('ignores a return address on another site', () => {
    serverRespondsWith(500, {})
    renderAt('/sign-in?returnUrl=%2F%2Fevil.example', <SignInPage />)

    expect(screen.getByRole('link', { name: 'Continue with Google' })).toHaveAttribute(
      'href',
      '/api/auth/google?returnUrl=%2F',
    )
  })
})
