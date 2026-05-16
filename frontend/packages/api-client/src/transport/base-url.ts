const defaultBrowserBaseUrl = ''
const defaultServerBaseUrl = 'http://localhost:5100'

export function getApiBaseUrl(env: ImportMetaEnv = import.meta.env): string {
  const configured = env.VITE_NERV_IIP_API_BASE_URL
  if (configured && configured.trim().length > 0) {
    return configured
  }

  if (typeof window === 'undefined') {
    return defaultServerBaseUrl
  }

  return defaultBrowserBaseUrl
}
