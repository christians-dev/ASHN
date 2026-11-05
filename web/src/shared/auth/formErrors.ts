import { ApiError } from '../../api/client'

export type FieldErrors = Record<string, string | undefined>

/** First message per field from a validation problem, e.g. { email: "Email is already taken." }. */
export function fieldErrorsFrom(error: unknown): FieldErrors {
  if (!(error instanceof ApiError) || !error.problem.errors) return {}
  return Object.fromEntries(
    Object.entries(error.problem.errors).map(([field, messages]) => [field, messages[0]]),
  )
}

/** A message for the whole form: the problem's detail, or a generic fallback. */
export function formMessageFrom(error: unknown): string {
  return error instanceof ApiError
    ? (error.problem.detail ?? 'Please check the form and try again.')
    : 'Something went wrong. Please try again.'
}

/** Only paths on this site, so a crafted link can't send people elsewhere after sign-in. */
export function safeReturnUrl(url: string | null): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\') ? url : '/'
}
