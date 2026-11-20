import { isForbiddenRequestError } from '@nerv-iip/api-client'
export class NoWorkScope extends Error {}

export function accessError(error: unknown): string | undefined {
  if (error instanceof NoWorkScope) return error.message
  if (isForbiddenRequestError(error)) return '无权查看当前范围'
  const status = error as { status?: number; response?: { status?: number } } | undefined
  if (status?.status === 401 || status?.response?.status === 401) return '登录已失效'
}
