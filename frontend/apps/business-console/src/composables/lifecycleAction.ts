import {
  getBusinessWriteErrorStatus,
  preserveBusinessWriteErrorStatus,
  statusActionGate,
  type LifecycleActionRequest,
} from '@nerv-iip/business-core'
import { computed, shallowRef } from 'vue'

export const LIFECYCLE_STATE_CHANGED_MESSAGE = '状态已被其他操作更新'

export type LifecycleStateChangedSource = 'preflight' | 'conflict'

export class LifecycleStateChangedError extends Error {
  readonly source: LifecycleStateChangedSource

  constructor(source: LifecycleStateChangedSource) {
    super(LIFECYCLE_STATE_CHANGED_MESSAGE)
    this.name = 'LifecycleStateChangedError'
    this.source = source
  }
}

export function isIndeterminateLifecycleWriteError(error: unknown) {
  const status = getBusinessWriteErrorStatus(error)
  if (status !== undefined) return status >= 500
  if (error instanceof TypeError) return true
  if (typeof DOMException !== 'undefined' && error instanceof DOMException) {
    return error.name === 'AbortError' || error.name === 'TimeoutError'
  }
  if (!(error instanceof Error)) return false
  return /failed to fetch|network\s?error|network interrupted|timeout|timed out|econn|connection reset/i.test(
    error.message,
  )
}

export function useLifecycleWriteIntent<TAction extends string>(
  makeKey: (taskId: string, action: TAction) => string,
) {
  type Intent = { taskId: string; action: TAction; key: string; unknown: boolean }
  const current = shallowRef<Intent | null>(null)
  const locked = computed(() => current.value?.unknown === true)

  function acquire(taskId: string, action: TAction) {
    const existing = current.value
    if (existing) {
      return existing.taskId === taskId && existing.action === action ? existing : undefined
    }
    const next: Intent = { taskId, action, key: makeKey(taskId, action), unknown: false }
    current.value = next
    return next
  }

  function clear() {
    current.value = null
  }

  function recordFailure(error: unknown) {
    if (!isIndeterminateLifecycleWriteError(error)) {
      clear()
      return false
    }
    if (current.value) current.value = { ...current.value, unknown: true }
    return true
  }

  function permits(taskId: string, action: TAction) {
    const existing = current.value
    return !existing || (existing.taskId === taskId && existing.action === action)
  }

  return { acquire, clear, current, locked, permits, recordFailure }
}

type CommandResult<TData> = Readonly<{
  data?: TData
  error?: unknown
  response?: Readonly<{ status: number }>
}>

type ExecuteLifecycleActionOptions<TData> = Readonly<{
  readLatest: () => Promise<LifecycleActionRequest | undefined>
  command: () => Promise<CommandResult<TData>>
}>

export async function executeLifecycleAction<TData>({
  readLatest,
  command,
}: ExecuteLifecycleActionOptions<TData>): Promise<TData | undefined> {
  const latest = await readLatest()
  const gate = latest ? statusActionGate(latest) : undefined
  if (!gate || (!gate.executable && !gate.legalNoop)) {
    throw new LifecycleStateChangedError('preflight')
  }

  let result: CommandResult<TData>
  try {
    result = await command()
  } catch (error) {
    if (getBusinessWriteErrorStatus(error) === 409) {
      throw new LifecycleStateChangedError('conflict')
    }
    throw error
  }
  if (result.response?.status === 409) {
    throw new LifecycleStateChangedError('conflict')
  }
  const envelopeError =
    result.data &&
    typeof result.data === 'object' &&
    'success' in result.data &&
    result.data.success === false
      ? result.data
      : undefined
  if (result.error !== undefined || envelopeError !== undefined) {
    const error = result.error ?? envelopeError
    preserveBusinessWriteErrorStatus(error, result.response?.status)
    throw error
  }

  return result.data
}

type LifecycleRecoveryOptions = Readonly<{
  reset: () => Promise<void> | void
  refresh: () => Promise<unknown>
  notify: (message: string) => void
}>

export async function recoverLifecycleAction(
  error: unknown,
  { reset, refresh, notify }: LifecycleRecoveryOptions,
): Promise<boolean> {
  if (!(error instanceof LifecycleStateChangedError)) return false

  await reset()
  try {
    await refresh()
  } catch {
    // 权威列表刷新是 best-effort；固定冲突提示不能被刷新故障吞掉。
  }
  notify(LIFECYCLE_STATE_CHANGED_MESSAGE)
  return true
}
