import { type ListBusinessConsoleMesWorkOrdersData } from '@nerv-iip/api-client'
import { useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { reactive } from 'vue'
import { bindBusinessContext, type BusinessContextFields } from '../businessContextBinding'

export const DEFAULT_TAKE = 100

export const MES_OPERATIONS_READ_PERMISSION = 'business.mes.operations.read'

export const MES_OPERATIONS_MANAGE_PERMISSION = 'business.mes.operations.manage'

export const MES_REPORTING_WRITE_PERMISSION = 'business.mes.reporting.write'

export const MES_WORK_ORDERS_READ_PERMISSION = 'business.mes.work-orders.read'

export const MES_WORK_ORDERS_MANAGE_PERMISSION = 'business.mes.work-orders.manage'

export const MES_WORK_SCOPE_REQUIRED_MESSAGE =
  '尚未选择已授权作业范围，当前操作已禁用。请在页面上方的「作业范围」选择器中选择后继续。'

export const MES_WORK_SCOPE_UNAVAILABLE_MESSAGE =
  '当前账号在本组织没有已授权的作业范围，无法读取现场数据。' +
  '请联系管理员为该账号配置数据范围（组织/车间/工作中心/班组/本人）后重新登录。'

export function requirePendingPayloadSnapshot<T extends object>(
  snapshot: unknown,
  operation: string,
): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少上次未完成提交的内容，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

// 取消补偿预览按此页大小完整分页，直到取全该工单的全部关联单据（取消 handler 处理全部）。
export const CANCEL_PREVIEW_PAGE_SIZE = 200

// 逐页拉取直到最后一页（返回不足一页即结束），累计全部行。任一页 fetch 失败（throwOnError:true）会向上抛，
// 由 useQuery.error 捕获，从而让补偿预览走失败门禁（不允许在不完整数据上确认取消）。
export async function fetchAllCompensationItems<TRow>(
  fetchPage: (skip: number, take: number) => Promise<TRow[]>,
): Promise<TRow[]> {
  const items: TRow[] = []
  let skip = 0
  for (;;) {
    const page = await fetchPage(skip, CANCEL_PREVIEW_PAGE_SIZE)
    items.push(...page)
    if (page.length < CANCEL_PREVIEW_PAGE_SIZE) {
      break
    }
    skip += page.length
  }
  return items
}

type MesListStatus = NonNullable<
  NonNullable<ListBusinessConsoleMesWorkOrdersData['query']>['status']
>

export interface MesListFilters {
  workOrderId?: string
  organizationId: string
  environmentId: string
  status?: string
  /** 多状态过滤（CSV，如 'created,released,started,hold'）；与单值 status 互补，排产候选池用。 */
  statuses?: string
  keyword?: string
  workCenterId?: string
  shiftId?: string
  deviceAssetId?: string
  /** 受派工人（派工看板按人筛选负荷用；facade 侧仅派工列表支持）。 */
  assignedUserId?: string
  source?: string
  readinessStatus?: string
  /** 停机原因码过滤（停机读面按原因筛选用）。 */
  reasonCode?: string
  windowStartUtc?: string
  windowEndUtc?: string
  skip: number
  take: number
}

export interface MesFoundationReadinessFilters {
  organizationId: string
  environmentId: string
  siteCode?: string
  lineCode?: string
  workCenterCode?: string
  skuId?: string
  productionVersionId?: string
  plannedStartUtc?: string
  plannedEndUtc?: string
}

export interface MesWorkOrderContext {
  organizationId: string
  environmentId: string
  workOrderId: string
}

export interface MesContextFilters extends BusinessContextFields {
  workOrderId?: string
}

export interface MesTraceabilityFilters extends MesContextFilters {
  workOrderId: string
  batchOrSerial: string
  materialLotId: string
  mode: 'work-order' | 'batch' | 'material-lot'
}

export function defaultFilters(initialTake = DEFAULT_TAKE): MesListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: initialTake,
    }),
  )
}

export function defaultContext(): MesContextFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

export function defaultFoundationFilters(): MesFoundationReadinessFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

export function defaultWorkOrderContext(): MesWorkOrderContext {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      workOrderId: '',
    }),
  )
}

export function defaultTraceabilityFilters(): MesTraceabilityFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      workOrderId: '',
      batchOrSerial: '',
      materialLotId: '',
      mode: 'work-order',
    }),
  )
}

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function toContextQuery(filters: MesContextFilters | MesWorkOrderContext) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
  }
}

export function isNonEmpty(value: string | undefined) {
  return value !== undefined && value.trim().length > 0
}

export function toFoundationQuery(filters: MesFoundationReadinessFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('siteCode', filters.siteCode),
    ...optionalQuery('lineCode', filters.lineCode),
    ...optionalQuery('workCenterCode', filters.workCenterCode),
    ...optionalQuery('skuId', filters.skuId),
    ...optionalQuery('productionVersionId', filters.productionVersionId),
    ...optionalQuery('plannedStartUtc', filters.plannedStartUtc),
    ...optionalQuery('plannedEndUtc', filters.plannedEndUtc),
  }
}

export function toListQuery(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status as MesListStatus | undefined),
    ...optionalQuery('statuses', filters.statuses),
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('shiftId', filters.shiftId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    ...optionalQuery('source', filters.source),
    ...optionalQuery('readinessStatus', filters.readinessStatus),
    skip: filters.skip,
    take: filters.take,
  }
}

export function toListQueryWithoutStatus(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('shiftId', filters.shiftId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    skip: filters.skip,
    take: filters.take,
  }
}

export function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

export function envelopeItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

// 注意：加载中 / 失败 / 查询未启用时信封都是 undefined，这里一律回 0——所以 `xxxTotal` 单独用
// 是**没有语义的**：0 既可能是「真的一条都没有」，也可能是「压根没取到」。页面要区分这两者，
// 必须配套读同名的 `xxxState`（见 ./businessReadState），只有 `ready` 时 0 才算数。
export function envelopeTotal<
  TEnvelope extends { success?: boolean; data?: { total?: number } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

export function ignoreBackgroundError(_error: unknown) {}

// 传输层幂等键。完工入库重试与质量 hold 强制释放的重复保护由后端状态机兜底
// （重试仅 InventoryPostingFailed 可发起、强制释放仅 Active hold 生效），此处每次动作取新键即可。
// 导出供创建完工入库按「登记会话」播种键（页面持有键、成功后轮换，见 mes/receipts.vue）。
export function makeIdempotencyKey(prefix: string): string {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return `${prefix}-${cryptoApi.randomUUID()}`
  }
  return `${prefix}-${Math.random().toString(36).slice(2)}`
}

export function invalidateMesQueries(
  queryCache: ReturnType<typeof useQueryCache>,
  ids: string[],
  refetchActive = true,
) {
  return Promise.all(
    ids.map((id) =>
      queryCache.invalidateQueries(
        {
          predicate: isBusinessQuery(id),
        },
        refetchActive,
      ),
    ),
  )
}

function invalidateWorkOrders(queryCache: ReturnType<typeof useQueryCache>) {
  return invalidateMesQueries(queryCache, ['listBusinessConsoleMesWorkOrders'])
}
