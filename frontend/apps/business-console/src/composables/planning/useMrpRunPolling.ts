import type { BusinessConsoleMrpRunItem } from '@nerv-iip/api-client'
import { getCurrentScope, onScopeDispose, reactive, type Ref } from 'vue'

/**
 * 运行 MRP 的异步跟踪状态（#1306）：提交即受理（202 + runId），
 * 之后轮询运行列表读面直到终态；弹框全程可关闭，后台继续跑。
 */
export interface ActiveMrpRunState {
  runId: string
  /** '' = 无进行中的运行。polling-timeout = 轮询超时但任务可能仍在后台执行。 */
  status: '' | 'queued' | 'running' | 'completed' | 'failed' | 'polling-timeout'
  failureReason: string
  suggestionCount: number | null
}

/** 轮询间隔与上限：MRP 实测十余秒量级，2s 步进、5 分钟封顶足够并且不刷爆网关。 */
export const MRP_RUN_POLL_INTERVAL_MS = 2_000
export const MRP_RUN_POLL_TIMEOUT_MS = 5 * 60 * 1_000

function normalizeMrpRunStatus(status: string | null | undefined): ActiveMrpRunState['status'] {
  const value = (status ?? '').toLowerCase()
  if (value === 'completed') return 'completed'
  if (value === 'failed') return 'failed'
  if (value === 'running') return 'running'
  // Created（服务端排队态）与未知值都按排队中处理，等待下一次轮询澄清。
  return 'queued'
}

export function useMrpRunPolling(
  runs: Readonly<Ref<readonly BusinessConsoleMrpRunItem[]>>,
  refetchRuns: () => Promise<unknown>,
  onTerminal: () => void,
) {
  // —— 运行 MRP 异步跟踪（#1306）——提交受理后轮询运行列表直到终态。
  const activeMrpRun = reactive<ActiveMrpRunState>({
    runId: '',
    status: '',
    failureReason: '',
    suggestionCount: null,
  })
  let mrpPollTimer: ReturnType<typeof setTimeout> | null = null
  let mrpPollDeadline = 0

  function stopMrpRunPolling() {
    if (mrpPollTimer !== null) {
      clearTimeout(mrpPollTimer)
      mrpPollTimer = null
    }
  }

  if (getCurrentScope()) {
    onScopeDispose(stopMrpRunPolling)
  }

  function applyActiveRunSnapshot(run: BusinessConsoleMrpRunItem | undefined): boolean {
    if (!run) {
      return false
    }

    activeMrpRun.status = normalizeMrpRunStatus(run.status)
    if (activeMrpRun.status === 'completed') {
      activeMrpRun.suggestionCount = run.suggestionCount ?? 0
    }
    if (activeMrpRun.status === 'failed') {
      activeMrpRun.failureReason = run.failureReason ?? ''
    }
    return activeMrpRun.status === 'completed' || activeMrpRun.status === 'failed'
  }

  async function pollActiveMrpRunOnce() {
    mrpPollTimer = null
    if (!activeMrpRun.runId) {
      return
    }

    await refetchRuns()

    const run = runs.value.find((item) => item.runId === activeMrpRun.runId)
    if (applyActiveRunSnapshot(run)) {
      // 终态：建议/KPI 等读面统一失效重取。
      onTerminal()
      return
    }

    if (Date.now() >= mrpPollDeadline) {
      // 轮询超时 ≠ 任务失败：后台可能仍在计算，把决定权还给用户（运行列表可回看）。
      activeMrpRun.status = 'polling-timeout'
      return
    }

    mrpPollTimer = setTimeout(() => void pollActiveMrpRunOnce(), MRP_RUN_POLL_INTERVAL_MS)
  }

  function trackMrpRun(runId: string) {
    stopMrpRunPolling()
    activeMrpRun.runId = runId
    activeMrpRun.status = 'queued'
    activeMrpRun.failureReason = ''
    activeMrpRun.suggestionCount = null
    mrpPollDeadline = Date.now() + MRP_RUN_POLL_TIMEOUT_MS
    mrpPollTimer = setTimeout(() => void pollActiveMrpRunOnce(), MRP_RUN_POLL_INTERVAL_MS)
  }

  return { activeMrpRun, trackMrpRun }
}
