import {
  statusActionGate,
  type LifecycleActionRequest,
  type StatusActionGate,
} from '@nerv-iip/business-core'
import { shallowRef } from 'vue'

export const LIFECYCLE_ACTION_UPDATED_MESSAGE = '状态已被其他操作更新'

export class LifecycleActionUnavailableError extends Error {
  readonly gate: StatusActionGate

  constructor(gate: StatusActionGate) {
    super(LIFECYCLE_ACTION_UPDATED_MESSAGE)
    this.name = 'LifecycleActionUnavailableError'
    this.gate = gate
  }
}

export function assertLifecycleActionExecutable(request: LifecycleActionRequest) {
  const gate = statusActionGate(request)
  if (!gate.executable && !gate.legalNoop) {
    throw new LifecycleActionUnavailableError(gate)
  }
  return gate
}

export function isLifecycleConflictError(
  error: unknown,
): error is Readonly<{ success: false; message: 'lifecycle-conflict' }> {
  return (
    typeof error === 'object' &&
    error !== null &&
    !(error instanceof Error) &&
    'success' in error &&
    error.success === false &&
    'message' in error &&
    error.message === 'lifecycle-conflict'
  )
}

export function isLifecycleActionUpdated(error: unknown) {
  return error instanceof LifecycleActionUnavailableError || isLifecycleConflictError(error)
}

export function useLifecycleActionRecovery(options: {
  reset: () => void
  refresh: () => Promise<unknown> | unknown
}) {
  const toast = shallowRef({
    show: false,
    message: '',
    type: 'error' as const,
  })

  async function handle(error: unknown) {
    if (!isLifecycleActionUpdated(error)) return false
    options.reset()
    try {
      await options.refresh()
    } catch {
      // 权威列表刷新是 best-effort；固定冲突提示不能被刷新故障吞掉。
    }
    toast.value = {
      show: true,
      message: LIFECYCLE_ACTION_UPDATED_MESSAGE,
      type: 'error',
    }
    return true
  }

  function setToastOpen(show: boolean) {
    toast.value = { ...toast.value, show }
  }

  return { toast, handle, setToastOpen }
}
