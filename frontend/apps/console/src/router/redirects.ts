const DEFAULT_REDIRECT_PATH = '/'

export function sanitizeRedirectPath(value: unknown): string {
  if (typeof value !== 'string') {
    return DEFAULT_REDIRECT_PATH
  }

  if (!value.startsWith('/') || value.startsWith('//')) {
    return DEFAULT_REDIRECT_PATH
  }

  if (value === '/login' || value.startsWith('/login?') || value.startsWith('/login#')) {
    return DEFAULT_REDIRECT_PATH
  }

  return value
}
