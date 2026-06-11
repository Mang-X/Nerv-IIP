const DEFAULT_REDIRECT_PATH = '/'

/**
 * 把不可信的 redirect 查询参数收敛为安全的内部路径，防开放重定向。
 * 只放行以单个 `/` 开头的内部路径；`//host` 与 `/\host`（浏览器都按
 * 协议相对 URL 解析到外站）以及绝对 URL、非字符串一律退回根路径。
 */
export function sanitizeRedirectPath(value: unknown): string {
  if (typeof value !== 'string') {
    return DEFAULT_REDIRECT_PATH
  }

  if (!value.startsWith('/')) {
    return DEFAULT_REDIRECT_PATH
  }

  if (value.startsWith('//') || value.startsWith('/\\')) {
    return DEFAULT_REDIRECT_PATH
  }

  return value
}
