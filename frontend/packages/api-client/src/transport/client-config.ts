import { client } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'

export interface ConfigureApiClientOptions {
  baseUrl?: string
  headers?: HeadersInit
}

export function configureApiClient(options: ConfigureApiClientOptions = {}): void {
  client.setConfig({
    baseUrl: options.baseUrl ?? getApiBaseUrl(),
    headers: options.headers,
  })
}
