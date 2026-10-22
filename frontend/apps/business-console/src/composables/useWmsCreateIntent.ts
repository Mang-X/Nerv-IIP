import { getBusinessWriteErrorStatus } from '@nerv-iip/business-core'
import { shallowRef } from 'vue'
import { createWmsIdempotencyKey } from './useBusinessWms'

/** 两处先分号再远程校验的创建：只有确定拒绝后，新内容才是新的提交。 */
export function useWmsCreateIntent<T extends object>() {
  let current: { payload: T; fingerprint: string; idempotencyKey: string } | undefined
  const locked = shallowRef(false)

  function reset() {
    current = undefined
    locked.value = false
  }

  function acquire(payload: T) {
    const fingerprint = JSON.stringify(payload)
    if (!current || (!locked.value && current.fingerprint !== fingerprint)) {
      current = { payload, fingerprint, idempotencyKey: createWmsIdempotencyKey() }
    }
    locked.value = true
    return current
  }

  function recordFailure(error: unknown) {
    const status = getBusinessWriteErrorStatus(error)
    // 408 与网络/5xx 一样无法确认是否落单；其余 HTTP 4xx 是明确拒绝。
    locked.value = !(status !== undefined && status >= 400 && status < 500 && status !== 408)
  }

  return { acquire, locked, recordFailure, reset }
}
