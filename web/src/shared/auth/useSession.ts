import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'

export type Session = Schemas['SessionResponse']

export const sessionQueryKey = ['session'] as const

/** Who is signed in. Refetch after sign-in/out: it also refreshes the anti-forgery cookie. */
export function useSession() {
  return useQuery({
    queryKey: sessionQueryKey,
    queryFn: () => unwrap(api.GET('/api/auth/session')),
    staleTime: 60_000,
  })
}

export const isAdmin = (session: Session | undefined) => session?.roles.includes('Admin') ?? false
