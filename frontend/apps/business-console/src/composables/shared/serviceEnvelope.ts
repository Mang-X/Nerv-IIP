/**
 * netcorepal 信封的共用断言。
 *
 * 网关可能回 200 + `success:false`（软失败）。各域 composable 若各写一份判断，
 * 迟早有一处漏掉，界面就显示假成功。这里收一处，写面 mutation 统一 `.then(assertEnvelopeSuccess)`。
 */
export interface ServiceEnvelope {
  success?: boolean
  message?: string | null
}

/** 软失败诚实上抛：优先透传服务端 message，没有就用调用方给的兜底话术。 */
export function assertEnvelopeSuccess<T extends ServiceEnvelope>(
  envelope: T,
  fallbackMessage: string,
): T {
  if (!envelope.success) {
    throw new Error(envelope.message || fallbackMessage)
  }
  return envelope
}
