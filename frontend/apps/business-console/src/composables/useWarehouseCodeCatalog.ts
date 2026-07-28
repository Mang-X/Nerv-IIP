/**
 * 库位 / 批次 / 序列号的**派生目录**。
 *
 * 背景：这三者是仓管填得最多、也最容易填错的字段，但后端目前**没有任何列表读面**——
 * 库位只有 `POST /api/inventory/v1/locations`（创建，网关都没代理），批次与序列号则完全没有
 * 独立目录端点。缺口已登记（见 `docs/architecture/inventory-module-product-design.md`）。
 *
 * 在后端补齐之前，这里不去凭空造目录，而是把**系统里已经真实存在的编码**收集起来：
 * 上架/拣货任务的起讫库位、盘点执行的库位、出库单行的库位/批次/序列号。
 * 这些都是真实业务数据，足以让仓管「从现有库位里挑」而不是凭记忆敲。
 *
 * 界面上必须如实说明来源（`WAREHOUSE_CATALOG_SOURCE_TEXT`），不要让人误以为这是库位主数据。
 * 因此这些字段保留 `clearable`，且**新建单据时若目标库位尚未在系统中出现过**，
 * 页面需另给「手动录入新库位」的出路——派生目录只覆盖已有编码，不能挡住开新库位。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import { computed } from 'vue'
import {
  useWmsCountExecutions,
  useWmsOutboundOrders,
  useWmsPickingTasks,
  useWmsPutawayTasks,
} from './useBusinessWms'

/** 派生目录的取数上限：覆盖近期作业即可，不必拉全历史。 */
const CATALOG_TAKE = 300
/**
 * 单个下拉最多列出的编码数。
 *
 * 这不是审美取舍，是硬性上限：`EntityPickerPanel` 用 `v-for` 铺**全部**选项，既不虚拟化也不分页。
 * 批次与预留页把整张台账（实测首屏 3811 行、全扫约 1.4 万行）当作 `extraLines` 喂进来，
 * 不设上限的话光是展开一次「批次」下拉就会同步渲染上万个节点，把主线程钉死。
 * 超出部分不是丢掉不管——`locationSourceText` / `lotSourceText` / `serialSourceText`
 * 会如实说明共有多少个编码、当前列出了多少。
 */
const CATALOG_OPTION_LIMIT = 500

export const WAREHOUSE_CATALOG_SOURCE_TEXT = '数据来自现有库存与仓储作业记录（暂无库位主数据）'
export const WAREHOUSE_LOCATION_EMPTY_TEXT = '系统里还没有出现过库位，可直接录入新库位编码'
export const WAREHOUSE_LOT_EMPTY_TEXT = '系统里还没有出现过批次'
export const WAREHOUSE_SERIAL_EMPTY_TEXT = '系统里还没有出现过序列号'

/** 页面自己已经加载的库存行也是一手来源，允许并入（如库存可用量的台账明细）。 */
export interface WarehouseCodeSourceLine {
  locationCode?: string | null
  lotNo?: string | null
  serialNo?: string | null
  skuCode?: string | null
}

interface CodeAccumulator {
  value: string
  hints: Set<string>
}

function collect(map: Map<string, CodeAccumulator>, code?: string | null, hint?: string | null) {
  const value = code?.trim()
  if (!value) return
  const entry = map.get(value) ?? { value, hints: new Set<string>() }
  const trimmedHint = hint?.trim()
  if (trimmedHint) entry.hints.add(trimmedHint)
  map.set(value, entry)
}

function toOptions(map: Map<string, CodeAccumulator>): EntityPickerOption[] {
  return [...map.values()]
    .sort((a, b) => a.value.localeCompare(b.value, 'zh-Hans-CN', { numeric: true }))
    .slice(0, CATALOG_OPTION_LIMIT)
    .map((entry) => {
      const hints = [...entry.hints]
      // hint 只放少量辅助识别信息，堆一长串反而看不清。
      const hint =
        hints.length > 3
          ? `${hints.slice(0, 3).join('、')} 等 ${hints.length} 项`
          : hints.join('、')
      return hint
        ? { value: entry.value, label: entry.value, hint }
        : { value: entry.value, label: entry.value }
    })
}

/**
 * @param extraLines 页面已加载的库存/单据行，作为额外来源并入目录
 */
export function useWarehouseCodeCatalog(extraLines?: () => WarehouseCodeSourceLine[]) {
  const putaway = useWmsPutawayTasks({ take: CATALOG_TAKE })
  const picking = useWmsPickingTasks({ take: CATALOG_TAKE })
  const counts = useWmsCountExecutions({ take: CATALOG_TAKE })
  const outbound = useWmsOutboundOrders({ take: CATALOG_TAKE })

  const locationMap = computed(() => {
    const map = new Map<string, CodeAccumulator>()
    for (const task of [...putaway.putawayTasks.value, ...picking.pickingTasks.value]) {
      collect(map, task.fromLocationCode, task.skuCode)
      collect(map, task.toLocationCode, task.skuCode)
    }
    for (const execution of counts.countExecutions.value) {
      collect(map, execution.locationCode, execution.skuCode)
    }
    for (const order of outbound.outboundOrders.value) {
      for (const line of order.lines ?? []) collect(map, line.locationCode, line.skuCode)
    }
    for (const line of extraLines?.() ?? []) collect(map, line.locationCode, line.skuCode)
    return map
  })

  const lotMap = computed(() => {
    const map = new Map<string, CodeAccumulator>()
    for (const order of outbound.outboundOrders.value) {
      for (const line of order.lines ?? []) collect(map, line.lotNo, line.skuCode)
    }
    for (const line of extraLines?.() ?? []) collect(map, line.lotNo, line.skuCode)
    return map
  })

  const serialMap = computed(() => {
    const map = new Map<string, CodeAccumulator>()
    for (const order of outbound.outboundOrders.value) {
      for (const line of order.lines ?? []) collect(map, line.serialNo, line.skuCode)
    }
    for (const line of extraLines?.() ?? []) collect(map, line.serialNo, line.skuCode)
    return map
  })

  const locationOptions = computed<EntityPickerOption[]>(() => toOptions(locationMap.value))
  const lotOptions = computed<EntityPickerOption[]>(() => toOptions(lotMap.value))
  const serialOptions = computed<EntityPickerOption[]>(() => toOptions(serialMap.value))

  /**
   * 来源说明——截断了就说截断了。
   * 下拉底部只会显示「共 N 条」（`N` = 实际列出的条数），若不在这里交代，
   * 用户会以为系统里就这么多编码，那是假信息。
   */
  function sourceTextFor(total: number) {
    return total > CATALOG_OPTION_LIMIT
      ? `${WAREHOUSE_CATALOG_SOURCE_TEXT}；共 ${total} 个编码，编码过多仅列出前 ${CATALOG_OPTION_LIMIT} 个，可直接搜索`
      : WAREHOUSE_CATALOG_SOURCE_TEXT
  }

  return {
    locationOptions,
    lotOptions,
    serialOptions,
    locationSourceText: computed(() => sourceTextFor(locationMap.value.size)),
    lotSourceText: computed(() => sourceTextFor(lotMap.value.size)),
    serialSourceText: computed(() => sourceTextFor(serialMap.value.size)),
    warehouseCatalogPending: computed(
      () =>
        putaway.putawayTasksPending.value ||
        picking.pickingTasksPending.value ||
        counts.countExecutionsPending.value ||
        outbound.outboundOrdersPending.value,
    ),
  }
}
