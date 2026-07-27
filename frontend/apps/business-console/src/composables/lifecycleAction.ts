import { statusActionGate, type LifecycleActionRequest } from '@nerv-iip/business-core'

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

  const result = await command()
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
    throw result.error ?? envelopeError
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
