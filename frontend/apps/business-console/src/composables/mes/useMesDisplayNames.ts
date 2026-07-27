import { computed } from 'vue'
import {
  useBusinessSkus,
  useBusinessMasterDataResources,
  useBusinessWorkers,
} from '@/composables/useBusinessMasterData'

export interface MesDisplayNameOptions {
  /**
   * 额外加载班次主数据（用于把 shiftId 解析成班次名）。
   * 默认关闭：只有展示班次列的页面才付这次请求。
   */
  shifts?: boolean
  /**
   * 额外加载员工名录（用于把 assignedUserId 解析成姓名）。
   * 默认关闭。网关派工请求不透传 AssignedUserName（见 MES 后端移交清单），
   * 控制台派出去的工序拿不到姓名，只能靠名录在前端补齐。
   */
  workers?: boolean
}

/**
 * MES 列表显示名前端解析（兜底 facade 当前为 null 的 *Name，见 #461）。
 * 用法：accessor 写 `r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无'`，
 * 后端回填 *Name 后自动优先用之、本兜底极少命中，可随后端落地移除。
 */
export function useMesDisplayNames(options: MesDisplayNameOptions = {}) {
  const { skus } = useBusinessSkus()
  const { resources: workCenters } = useBusinessMasterDataResources('work-center')
  const shiftSource = options.shifts ? useBusinessMasterDataResources('shift') : undefined
  const workerSource = options.workers
    ? useBusinessWorkers({ employmentStatus: 'active' })
    : undefined

  const skuByCode = computed(() => {
    const m = new Map<string, string>()
    for (const s of skus.value) if (s.code) m.set(s.code, s.displayName ?? s.code)
    return m
  })
  const workCenterByCode = computed(() => {
    const m = new Map<string, string>()
    for (const w of workCenters.value) if (w.code) m.set(w.code, w.displayName ?? w.code)
    return m
  })
  const shiftByCode = computed(() => {
    const m = new Map<string, string>()
    for (const s of shiftSource?.resources.value ?? []) {
      if (s.code) m.set(s.code, s.displayName ?? s.code)
    }
    return m
  })
  // 名录同时按 userId 与工号建索引：facade 回的是 userId，人读单据上常写工号。
  const workerById = computed(() => {
    const m = new Map<string, string>()
    for (const w of workerSource?.workers.value ?? []) {
      const name = w.displayName ?? w.employeeNo
      if (!name) continue
      if (w.userId) m.set(w.userId, name)
      if (w.employeeNo) m.set(w.employeeNo, name)
    }
    return m
  })

  function resolveSku(code?: string | null): string | undefined {
    if (!code) return undefined
    return skuByCode.value.get(code) ?? code
  }
  /**
   * 物料展示串：优先主数据显示名，其次 facade 返回的人读编码。
   * 若拿到的是系统 GUID（部分 facade 仍回内部标识），一律不上屏——
   * 界面不暴露工程标识，读者也无法用它去任何地方查物料。
   */
  function resolveSkuLabel(value?: string | null): string {
    if (!value || SYSTEM_ID_PATTERN.test(value)) return '未指定物料'
    return skuByCode.value.get(value) ?? value
  }
  function resolveWorkCenter(code?: string | null): string | undefined {
    if (!code) return undefined
    return workCenterByCode.value.get(code) ?? code
  }
  /** 班次展示串；GUID 一律不上屏（同 resolveSkuLabel 口径）。 */
  function resolveShiftLabel(value?: string | null): string {
    if (!value) return '未排班'
    if (SYSTEM_ID_PATTERN.test(value)) return '未排班'
    return shiftByCode.value.get(value) ?? value
  }
  /**
   * 受派工人姓名；名录里查不到就返回 undefined，由调用方决定说法——
   * 不要在这里编一个「未知工人」，那会把「没派过」和「派了但没回填姓名」混为一谈。
   */
  function resolveWorker(userId?: string | null): string | undefined {
    if (!userId) return undefined
    return workerById.value.get(userId)
  }

  return {
    resolveShiftLabel,
    resolveSku,
    resolveSkuLabel,
    resolveWorkCenter,
    resolveWorker,
  }
}

/** 系统内部标识（GUID）形态，用于判断某个值是否可以上屏。 */
const SYSTEM_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
