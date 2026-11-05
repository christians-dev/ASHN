import createClient, { type Middleware } from 'openapi-fetch'
import type { components, paths } from './schema'

export type Schemas = components['schemas']

/** RFC 9457 problem details returned by the API for every error. */
export interface Problem {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: Problem

  constructor(status: number, problem: Problem | undefined) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`)
    this.status = status
    this.problem = problem ?? {}
  }
}

export const XSRF_COOKIE = 'XSRF-TOKEN'
export const XSRF_HEADER = 'X-XSRF-TOKEN'

function readCookie(name: string): string | undefined {
  return document.cookie
    .split('; ')
    .find((c) => c.startsWith(`${name}=`))
    ?.slice(name.length + 1)
}

// The API rejects writes without the anti-forgery token it put in the XSRF-TOKEN cookie (research R2).
const antiforgery: Middleware = {
  onRequest({ request }) {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method)) {
      const token = readCookie(XSRF_COOKIE)
      if (token) request.headers.set(XSRF_HEADER, decodeURIComponent(token))
    }
    return request
  },
}

/** Typed client generated from the API's OpenAPI document; paths include the /api prefix. */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  credentials: 'same-origin',
  // Look fetch up on each call (not once at start-up) so tests can replace it.
  fetch: (request) => globalThis.fetch(request),
})
api.use(antiforgery)

/** Returns the response body, or throws an ApiError carrying the problem details. */
export async function unwrap<T>(
  request: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await request
  if (!response.ok) throw new ApiError(response.status, error as Problem | undefined)
  return data as T
}
