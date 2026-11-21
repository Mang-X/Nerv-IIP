export type FulfillmentFailureKind = 'conflict' | 'timeout' | 'error'

// 单源失败载体：把 HTTP 语义带到 useQuery.error，供状态机分类。
export class FulfillmentNodeError extends Error {
  constructor(
    readonly httpStatus: number | 'network',
    message?: string,
  ) {
    super(message)
    this.name = 'FulfillmentNodeError'
  }
}

export interface SdkCallResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

// 用 generated sdk 原始函数（throwOnError:false）拿到 response.status，才能区分 403/409/超时。
export async function runNodeSource<T>(
  call: () => Promise<SdkCallResult<T>>,
): Promise<T | undefined> {
  let result: SdkCallResult<T>
  try {
    result = await call()
  } catch (cause) {
    // fetch 层抛错（超时 / 网络中断 / abort）— 归为 network 失败域。
    throw new FulfillmentNodeError('network', cause instanceof Error ? cause.message : undefined)
  }
  const status = result.response?.status ?? 0
  if (status === 403) {
    throw new FulfillmentNodeError(403, '无权查看该节点')
  }
  // 404 视为「尚未产生」（空态），不是失败。
  if (status === 404) {
    return undefined
  }
  if (result.error !== undefined || (status !== 0 && status >= 400)) {
    throw new FulfillmentNodeError(
      status || 'network',
      result.error instanceof Error ? result.error.message : undefined,
    )
  }
  return result.data
}

// 防御式读取错误上的 HTTP 状态（兼容 FulfillmentNodeError 与裸对象）。
export function httpStatusOf(error: unknown): number | 'network' | undefined {
  if (error instanceof FulfillmentNodeError) return error.httpStatus
  if (typeof error === 'object' && error !== null) {
    const record = error as Record<string, unknown>
    const raw =
      record.status ?? record.statusCode ?? (record.response as Response | undefined)?.status
    if (typeof raw === 'number') return raw
  }
  return undefined
}

/** 把单源错误分类为 restricted（403）或 failed + 失败子类。 */
export function classifyFulfillmentFailure(error: unknown): {
  status: 'restricted' | 'failed'
  failureKind?: FulfillmentFailureKind
} {
  const status = httpStatusOf(error)
  if (status === 403) return { status: 'restricted' }
  if (status === 409) return { status: 'failed', failureKind: 'conflict' }
  if (status === 408 || status === 504 || status === 'network') {
    return { status: 'failed', failureKind: 'timeout' }
  }
  return { status: 'failed', failureKind: 'error' }
}
