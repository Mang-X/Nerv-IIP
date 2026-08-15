import { client as businessConsoleClient } from '../generated/business-console/client.gen'
import { client as platformClient } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'

export interface ConfigureApiClientOptions {
  accessTokenProvider?: () => string | undefined
  baseUrl?: string
  fetch?: typeof fetch
  headers?: HeadersInit
  localeProvider?: () => string | undefined
  onUnauthorized?: () => void
}

interface GeneratedApiClient {
  interceptors: {
    request: {
      eject: (id: number) => void
      use: (handler: (request: Request) => Request) => number
    }
    response: {
      eject: (id: number) => void
      use: (handler: (response: Response) => Response) => number
    }
    error: {
      eject: (id: number) => void
      use: (handler: (error: unknown, response?: Response) => unknown) => number
    }
  }
  setConfig: (config: { baseUrl?: string; fetch?: typeof fetch; headers?: HeadersInit }) => void
}

const clients: GeneratedApiClient[] = [platformClient, businessConsoleClient]

let requestInterceptorIds: Array<number | undefined> = []
let responseInterceptorIds: Array<number | undefined> = []
let errorInterceptorIds: Array<number | undefined> = []
let managedHeaderNames = new Set<string>()

function wrapPrimitiveResponseError(error: unknown, response: Response) {
  const wrapped = new Error(typeof error === 'string' ? error : String(error))
  Object.defineProperties(wrapped, {
    cause: {
      configurable: true,
      enumerable: false,
      value: error,
    },
    response: {
      configurable: true,
      enumerable: false,
      value: response,
    },
    status: {
      configurable: true,
      enumerable: false,
      value: response.status,
    },
  })
  return wrapped
}

export function configureApiClient(options: ConfigureApiClientOptions = {}): void {
  clients.forEach((client, index) => {
    const requestInterceptorId = requestInterceptorIds[index]
    if (requestInterceptorId !== undefined) {
      client.interceptors.request.eject(requestInterceptorId)
      requestInterceptorIds[index] = undefined
    }

    const responseInterceptorId = responseInterceptorIds[index]
    if (responseInterceptorId !== undefined) {
      client.interceptors.response.eject(responseInterceptorId)
      responseInterceptorIds[index] = undefined
    }

    const errorInterceptorId = errorInterceptorIds[index]
    if (errorInterceptorId !== undefined) {
      client.interceptors.error.eject(errorInterceptorId)
      errorInterceptorIds[index] = undefined
    }
  })

  const configuredHeaders = new Headers(options.headers)
  const headerConfig: Record<string, string | null> = Object.fromEntries(
    [...managedHeaderNames].map((headerName) => [headerName, null]),
  )
  configuredHeaders.forEach((value, headerName) => {
    headerConfig[headerName] = value
  })
  managedHeaderNames = new Set(configuredHeaders.keys())

  const baseUrl = options.baseUrl ?? getApiBaseUrl()

  requestInterceptorIds = clients.map((client) => {
    client.setConfig({
      baseUrl,
      fetch: options.fetch,
      headers: headerConfig as unknown as HeadersInit,
    })

    return client.interceptors.request.use((request) => {
      const headers = new Headers(request.headers)

      const token = options.accessTokenProvider?.()
      const locale = options.localeProvider?.()

      if (token) {
        headers.set('Authorization', `Bearer ${token}`)
      }

      if (locale && !headers.has('Accept-Language')) {
        headers.set('Accept-Language', locale)
      }

      return new Request(request, { headers })
    })
  })

  responseInterceptorIds = clients.map((client) =>
    client.interceptors.response.use((response) => {
      if (response.status === 401) {
        options.onUnauthorized?.()
      }

      return response
    }),
  )

  errorInterceptorIds = clients.map((client) =>
    client.interceptors.error.use((error, response) => {
      if (!response) return error
      if (error === null || (typeof error !== 'object' && typeof error !== 'function')) {
        return wrapPrimitiveResponseError(error, response)
      }

      const candidate = error as { response?: unknown }
      if (candidate.response !== undefined) return error
      try {
        Object.defineProperty(candidate, 'response', {
          configurable: true,
          enumerable: false,
          value: response,
        })
        return error
      } catch {
        return error
      }
    }),
  )
}
