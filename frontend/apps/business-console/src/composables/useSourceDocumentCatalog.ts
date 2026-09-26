/**
 * 来源单据目录：按单据类型分派到各业务域现成的列表端点，把行映射成 `EntityPickerOption`。
 *
 * 背景：条码打印 / 扫码补录、检验记录、销售订单、成本候选、不合格品关闭
 * 这些表单都要填「来源单据」，过去全靠手输单号。来源单据分多种类型，每种类型各有现成的
 * 列表端点，所以这里不补后端聚合端点，而是「先选类型，再按该类型的列表搜单号」。
 *
 * 口径：`value` 是下游真正比对的那个值，逐类核对过，和过去手填时的值一致——
 * 大多是人读单号；维修工单只有系统 ID，显示时换成人读单号。
 * 没有可搜列表端点的类型（采购收货、销售退货、库存调拨、库存移动等）不在这里，页面退回自由输入。
 */
import {
  listBusinessConsoleErpQuotationsQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  listBusinessConsoleQualityInspectionRecordsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsSupplierReturnRequestsQueryOptions,
} from '@nerv-iip/api-client'
import { qualitySourceTypeLabel } from '@nerv-iip/business-core'
import type { EntityPickerOption } from '@nerv-iip/ui'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, ref, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from './businessContextBinding'
import { maintenanceWorkOrderNo, maintenanceWorkOrderOption } from './useEquipmentPickerCatalog'
import { useWmsWorkScope, type WmsWorkScopeCatalogKind } from './useWmsWorkScope'

/** 一次取回的候选条数；更多的靠搜索收窄，匹配总数如实交给选择器提示。 */
const PAGE_SIZE = 50
/** 维修工单列表不支持关键字搜索，取最近一批在本地过滤。 */
const LOCAL_FILTER_TAKE = 200

interface CatalogQuery {
  organizationId: string
  environmentId: string
  take: number
  keyword?: string
  scopeKind?: string
  scopeId?: string
}

interface ListEnvelope<TRow> {
  success?: boolean
  data?: { items?: TRow[] | null; total?: number } | null
}

interface CatalogSpecFields {
  /** 单据名词，用于标题、占位与空态文案。 */
  noun: string
  sourceText: string
  searchPlaceholder: string
  /** 端点支持按单号关键字搜索时走服务端搜索；否则取一批在本地过滤。 */
  serverSearch: boolean
  /** 列表按 WMS 作业范围授权时，取哪一类作业范围。 */
  wmsWorkScope?: WmsWorkScopeCatalogKind
}

interface TypedCatalogSpec<TRow> extends CatalogSpecFields {
  /** 生成的列表查询选项；行类型从它的返回值推出，`toOption` 读的字段因此受契约类型检查。 */
  queryOptions: (query: CatalogQuery) => {
    query: (context: never) => Promise<ListEnvelope<TRow>>
  }
  /** 行 → 选项；返回 `undefined` 表示这一行不能作为来源单据（如已转订单的报价）。 */
  toOption: (row: TRow) => EntityPickerOption | undefined
}

/** 运行期只按信封形状取行、交回同一条 spec 的 `toOption`，不再需要行类型。 */
interface SourceDocumentCatalogSpec extends CatalogSpecFields {
  queryOptions: (query: CatalogQuery) => object
  toOption: (row: unknown) => EntityPickerOption | undefined
}

/** 定义处按行类型检查，收进表里时擦成统一形状（行只会交回推出它的那条 spec）。 */
function defineSpec<TRow>(spec: TypedCatalogSpec<TRow>): SourceDocumentCatalogSpec {
  return spec as unknown as SourceDocumentCatalogSpec
}

function documentOption(
  value: string | null | undefined,
  label: string | null | undefined,
  ...hints: (string | null | undefined)[]
): EntityPickerOption | undefined {
  const optionValue = value?.trim()
  if (!optionValue) return undefined
  const hint = hints
    .map((part) => part?.trim())
    .filter(Boolean)
    .join(' · ')
  return { value: optionValue, label: label?.trim() || optionValue, ...(hint ? { hint } : {}) }
}

const SPECS = {
  // 生产工单：制造执行的工单 ID 就是工单号（列表的 workOrderNo 与它同值），
  // 条码生产标签、检验结论回写质量保留都按它比对。
  'mes-work-order': defineSpec({
    noun: '生产工单',
    sourceText: '数据来自制造执行生产工单',
    searchPlaceholder: '搜索工单…',
    serverSearch: true,
    queryOptions: (query) => listBusinessConsoleMesWorkOrdersQueryOptions({ query }),
    toOption: (row) => documentOption(row.workOrderId, row.workOrderNo, row.skuCode),
  }),
  'mes-production-report': defineSpec({
    noun: '报工单',
    sourceText: '数据来自制造执行报工记录',
    searchPlaceholder: '搜索报工单号 / 工单…',
    serverSearch: true,
    queryOptions: (query) => listBusinessConsoleMesProductionReportsQueryOptions({ query }),
    toOption: (row) => documentOption(row.reportNo, row.reportNo, row.workOrderNo),
  }),
  'wms-inbound-order': defineSpec({
    noun: '入库单',
    sourceText: '数据来自当前作业范围的仓储入库单',
    searchPlaceholder: '搜索入库单号…',
    serverSearch: true,
    queryOptions: (query) => listBusinessConsoleWmsInboundOrdersQueryOptions({ query }),
    wmsWorkScope: 'receipts',
    toOption: (row) => documentOption(row.inboundOrderNo, row.inboundOrderNo, row.siteCode),
  }),
  'wms-supplier-return': defineSpec({
    noun: '供应商退货单',
    sourceText: '数据来自仓储供应商退货',
    searchPlaceholder: '搜索退货单号 / 入库单号…',
    serverSearch: true,
    queryOptions: (query) => listBusinessConsoleWmsSupplierReturnRequestsQueryOptions({ query }),
    toOption: (row) =>
      documentOption(row.supplierReturnNo, row.supplierReturnNo, row.inboundOrderNo, row.skuCode),
  }),
  // 已批准且还没转过订单的报价单：转过的再转会被拒（一张报价只能转一张订单）。
  'erp-approved-quotation': defineSpec({
    noun: '已批准报价单',
    sourceText: '数据来自经营管理已批准的报价单',
    searchPlaceholder: '搜索报价单号…',
    serverSearch: true,
    queryOptions: (query) =>
      listBusinessConsoleErpQuotationsQueryOptions({ query: { ...query, status: 'Approved' } }),
    toOption: (row) =>
      row.convertedSalesOrderNo?.trim()
        ? undefined
        : documentOption(
            row.quotationNo,
            row.quotationNo,
            row.customerCode,
            row.expiresOn && `有效期至 ${row.expiresOn}`,
          ),
  }),
  // 质量检验：条码侧记的是「被检验的那张单据」（检验记录的来源单据），与检验页互链同口径。
  // 维修检验的来源单据是维修工单 ID，显示时换成人读单号。检验记录列表的关键字按物料编码过滤。
  'quality-inspection': defineSpec({
    noun: '检验对象',
    sourceText: '数据来自质量检验记录的来源单据',
    searchPlaceholder: '按物料编码搜索…',
    serverSearch: true,
    queryOptions: (query) => listBusinessConsoleQualityInspectionRecordsQueryOptions({ query }),
    toOption: (row) =>
      documentOption(
        row.sourceDocumentId,
        row.sourceType === 'maintenance'
          ? maintenanceWorkOrderNo(row.sourceDocumentId)
          : row.sourceDocumentId,
        row.sourceType && qualitySourceTypeLabel(row.sourceType),
        row.skuCode,
      ),
  }),
  // 维修工单只有系统 ID：提交 ID，显示人读单号（与维护页的工单选择器同一个映射）。
  'maintenance-work-order': defineSpec({
    noun: '维修工单',
    sourceText: '数据来自设备维护维修工单',
    searchPlaceholder: '搜索工单号…',
    serverSearch: false,
    queryOptions: (query) => listBusinessConsoleMaintenanceWorkOrdersQueryOptions({ query }),
    toOption: maintenanceWorkOrderOption,
  }),
}

export type SourceDocumentKind = keyof typeof SPECS

/**
 * 取某一类来源单据的候选。类型在一次挂载内固定：换类型时调用方整块重建（`key` 换成新类型），
 * 这样见过的项、搜索词和作业范围都不会串到另一类。
 * 已选值不在当前结果里时补一条占位（换过搜索词的用见过的名称），避免显示成「未选择」。
 */
export function useSourceDocumentCatalog(
  kind: SourceDocumentKind,
  selected: MaybeRefOrGetter<string>,
) {
  const context = useBusinessContextStore()
  const spec: SourceDocumentCatalogSpec = SPECS[kind]
  // 入库单列表按 WMS 作业范围授权，不带范围会被网关拒绝；沿用 WMS 页记住的范围。
  const wmsScope = spec.wmsWorkScope ? useWmsWorkScope(spec.wmsWorkScope) : undefined
  const search = ref('')
  const keyword = refDebounced(
    computed(() => search.value.trim()),
    300,
  )

  const query = useQuery(() => {
    const serverKeyword = spec.serverSearch ? keyword.value : ''
    const scopeKind = wmsScope?.scopeKind.value
    const scopeId = wmsScope?.scopeId.value
    return {
      ...(spec.queryOptions({
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        take: spec.serverSearch ? PAGE_SIZE : LOCAL_FILTER_TAKE,
        ...(serverKeyword ? { keyword: serverKeyword } : {}),
        ...(scopeKind && scopeId ? { scopeKind, scopeId } : {}),
      }) as object),
      enabled: hasBusinessContext(context) && (!wmsScope || wmsScope.hasSelection.value),
    } as never
  })

  const response = computed(() => {
    const envelope = query.data.value as ListEnvelope<unknown> | undefined
    return envelope?.success ? envelope.data : undefined
  })

  const results = computed<EntityPickerOption[]>(() => {
    const seen = new Set<string>()
    const rows: EntityPickerOption[] = []
    for (const row of response.value?.items ?? []) {
      const option = spec.toOption(row)
      if (!option || seen.has(option.value)) continue
      seen.add(option.value)
      rows.push(option)
    }
    return rows
  })

  const known = shallowRef(new Map<string, EntityPickerOption>())
  watch(results, (rows) => {
    if (!rows.length) return
    const next = new Map(known.value)
    for (const row of rows) next.set(row.value, row)
    known.value = next
  })

  const options = computed<EntityPickerOption[]>(() => {
    const current = toValue(selected).trim()
    if (!current || results.value.some((row) => row.value === current)) return results.value
    return [known.value.get(current) ?? { value: current, label: current }, ...results.value]
  })

  return {
    spec,
    search,
    options,
    pending: query.isLoading,
    total: computed(() => response.value?.total ?? 0),
  }
}
