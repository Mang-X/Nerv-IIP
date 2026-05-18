import { client } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'

export interface ConfigureApiClientOptions {
  accessTokenProvider?: () => string | undefined
  baseUrl?: string
  fetch?: typeof fetch
  headers?: HeadersInit
  onUnauthorized?: () => void
}

let requestInterceptorId: number | undefined
let responseInterceptorId: number | undefined
let managedHeaderNames = new Set<string>()

export function configureApiClient(options: ConfigureApiClientOptions = {}): void {
  if (requestInterceptorId !== undefined) {
    client.interceptors.request.eject(requestInterceptorId)
    requestInterceptorId = undefined
  }

  if (responseInterceptorId !== undefined) {
    client.interceptors.response.eject(responseInterceptorId)
    responseInterceptorId = undefined
  }

  const configuredHeaders = new Headers(options.headers)
  const headerConfig: Record<string, string | null> = Object.fromEntries(
    [...managedHeaderNames].map((headerName) => [headerName, null]),
  )
  configuredHeaders.forEach((value, headerName) => {
    headerConfig[headerName] = value
  })
  managedHeaderNames = new Set(configuredHeaders.keys())

  client.setConfig({
    baseUrl: options.baseUrl ?? getApiBaseUrl(),
    fetch: options.fetch,
    headers: headerConfig as unknown as HeadersInit,
  })

  requestInterceptorId = client.interceptors.request.use((request) => {
    const headers = new Headers(request.headers)

    const token = options.accessTokenProvider?.()

    if (token) {
      headers.set('Authorization', `Bearer ${token}`)
    } else {
      headers.delete('Authorization')
    }

    return new Request(request, { headers })
  })

  responseInterceptorId = client.interceptors.response.use((response) => {
    if (response.status === 401) {
      options.onUnauthorized?.()
    }

    return response
  })
}
