import { useQueryClient } from '@tanstack/react-query'
import { sessionQueryKey } from './useSession'

/**
 * Call after signing in, out or deleting the account: reloads who is signed in and, with it,
 * the anti-forgery token, which belongs to the user and is wrong for the next write otherwise.
 */
export function useRefreshSession() {
  const queryClient = useQueryClient()
  return () => queryClient.refetchQueries({ queryKey: sessionQueryKey })
}
