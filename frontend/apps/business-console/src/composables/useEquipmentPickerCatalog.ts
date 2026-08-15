/**
 * 设备域（设备监控 IoT + 维护）的选择器目录：把主数据 / 遥测 / 维护读面映射成 `EntityPickerOption`。
 *
 * 背景：遥测与维护的筛选条和录入弹窗过去要求手输设备编号、采集标签、单位、连接器实例、
 * 工单号——这些都是系统里已有的实体，敲错一个字符就查不到数或提交后才报错。这里按
 * 「一域一目录」收敛，各页只声明用哪几组目录。
 *
 * 口径：`value` 一律是提交体真正需要的标识（设备编号 / tagKey / 单位编码 / 工单 ID），
 * `label` 是人读名称，`hint` 放辅助识别信息（单位 / 值类型 / 状态 / 归属设备）。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import { computed, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useMaintenancePlans, useMaintenanceWorkOrders } from './useBusinessMaintenance'
import { toBaseUomBySku } from './skuBaseUom'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
  useBusinessUoms,
} from './useBusinessMasterData'
import { useBusinessTelemetryConnectors, useBusinessTelemetryTags } from './useBusinessTelemetry'

/** 主数据目录取数上限——一个厂区的设备 / 物料 / 单位目录量级在数百条。 */
const CATALOG_TAKE = 500
/** 单据目录取数上限：工单 / 保养计划只在「最近的在办单据」里挑，不做全量拉取。 */
const DOCUMENT_CATALOG_TAKE = 200

function toOption(
  code?: string | null,
  name?: string | null,
  hint?: string | null,
): EntityPickerOption[] {
  const value = code?.trim()
  if (!value) return []
  const label = name?.trim() || value
  const trimmedHint = hint?.trim()
  return [{ value, label, ...(trimmedHint ? { hint: trimmedHint } : {}) }]
}

function byLabel(a: EntityPickerOption, b: EntityPickerOption) {
  return a.label.localeCompare(b.label, 'zh-Hans-CN')
}

function joinHint(...parts: (string | null | undefined)[]) {
  return parts
    .map((part) => (part ?? '').trim())
    .filter(Boolean)
    .join(' · ')
}

/**
 * 采集标签的中文名对照。遥测读面只回工程标识 `tagKey`（`spindle-speed` / `bath-ph`），
 * 后端没有标签名字段，所以这里用常量把常见测点译成业务语言；对不上的仍原样显示
 * tagKey（不臆造名称），新增测点扩这张表即可。
 */
export const TELEMETRY_TAG_LABELS: Record<string, string> = {
  'air-pressure': '气压',
  'bath-ph': '槽液 pH',
  'bath-temperature': '槽液温度',
  'bearing-temperature': '轴承温度',
  'coolant-flow': '冷却液流量',
  current: '电流',
  'cycle-count': '循环计数',
  'damping-force': '阻尼力',
  flow: '流量',
  humidity: '湿度',
  level: '液位',
  power: '功率',
  'press-force': '压装力',
  pressure: '压力',
  runtime: '运行时长',
  'runtime-state': '运行状态',
  speed: '转速',
  'spindle-speed': '主轴转速',
  'spindle-temperature': '主轴温度',
  state: '运行状态',
  'temp-bearing': '轴承温度',
  temperature: '温度',
  torque: '扭矩',
  vibration: '振动',
  voltage: '电压',
  'weld-current': '焊接电流',
  'wheel-speed': '砂轮转速',
}

const TELEMETRY_VALUE_TYPE_LABELS: Record<string, string> = {
  bool: '布尔',
  boolean: '布尔',
  number: '数值',
  numeric: '数值',
  text: '文本',
}

/** tagKey 的分隔符各连接器不统一（`.` / `_` / `-`），对照表按连字符归一后查。 */
function normalizeTagKey(tagKey: string) {
  return tagKey
    .trim()
    .toLowerCase()
    .replace(/[._\s]+/g, '-')
}

/** 采集标签的人读名：对照表命中就用中文名，否则如实回落到 tagKey。 */
export function telemetryTagLabel(tagKey?: string | null) {
  const key = (tagKey ?? '').trim()
  if (!key) return ''
  return TELEMETRY_TAG_LABELS[normalizeTagKey(key)] ?? key
}

function telemetryValueTypeLabel(valueType?: string | null) {
  const key = (valueType ?? '').trim().toLowerCase()
  if (!key) return ''
  return TELEMETRY_VALUE_TYPE_LABELS[key] ?? key
}

/** 设备资产目录：遥测筛选条与维护建单共用的「哪台设备」。 */
export function useEquipmentDeviceCatalog() {
  const deviceCatalog = useBusinessMasterDataResources('device-asset')
  deviceCatalog.filters.take = CATALOG_TAKE

  return {
    deviceOptions: computed<EntityPickerOption[]>(() =>
      deviceCatalog.resources.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, row.workCenterCode ?? row.lineCode))
        .sort(byLabel),
    ),
    devicesPending: deviceCatalog.resourcesPending,
  }
}

/**
 * 采集标签目录，**跟随已选设备联动**：传入设备编号就只列该设备已配置的采集标签，
 * 没选设备时列全部标签（换设备时目录自动重取，不会把上一台设备的测点留在选项里）。
 *
 * `unitByTagKey` 让调用方在选完标签后自动带出该测点的单位，省掉一次手输。
 */
export function useTelemetryTagCatalog(deviceAssetId: MaybeRefOrGetter<string>) {
  const tagCatalog = useBusinessTelemetryTags({ take: CATALOG_TAKE })

  watch(
    () => toValue(deviceAssetId).trim(),
    (code) => {
      tagCatalog.filters.deviceAssetId = code
    },
    { immediate: true },
  )

  return {
    tagOptions: computed<EntityPickerOption[]>(() =>
      tagCatalog.tags.value
        .flatMap((row) =>
          toOption(
            row.tagKey,
            telemetryTagLabel(row.tagKey),
            joinHint(row.unitCode, telemetryValueTypeLabel(row.valueType)),
          ),
        )
        .sort(byLabel),
    ),
    tagsPending: tagCatalog.tagsPending,
    /** tagKey → 该测点标注的单位编码，用于选完标签自动带出单位。 */
    unitByTagKey: computed(() => {
      const map = new Map<string, string>()
      for (const row of tagCatalog.tags.value) {
        const key = row.tagKey?.trim()
        const unit = row.unitCode?.trim()
        if (key && unit) map.set(key, unit)
      }
      return map
    }),
  }
}

/** 工作中心目录：可用窗口按工作中心缩小范围。 */
export function useEquipmentWorkCenterCatalog() {
  const workCenterCatalog = useBusinessMasterDataResources('work-center')
  workCenterCatalog.filters.take = CATALOG_TAKE

  return {
    workCenterOptions: computed<EntityPickerOption[]>(() =>
      workCenterCatalog.resources.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, row.lineCode ?? row.workshopCode))
        .sort(byLabel),
    ),
    workCentersPending: workCenterCatalog.resourcesPending,
  }
}

/** 计量单位目录：报警阈值单位、点检测量单位、备件数量单位共用。 */
export function useEquipmentUomCatalog() {
  const uomCatalog = useBusinessUoms()
  uomCatalog.filters.take = CATALOG_TAKE

  return {
    uomOptions: computed<EntityPickerOption[]>(() =>
      uomCatalog.uoms.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName))
        .sort(byLabel),
    ),
    uomsPending: uomCatalog.uomsPending,
  }
}

/**
 * 班组目录：保养计划的负责班组。
 * 计划的 owner 后端存的是自由文本标签（读面不回该字段），所以 `value` 取班组名称本身，
 * 编码放 `hint` 供核对——不把 `TEAM-xxx` 这种编码写进人读字段。
 */
export function useEquipmentTeamCatalog() {
  const teamCatalog = useBusinessMasterDataResources('team')
  teamCatalog.filters.take = CATALOG_TAKE

  return {
    teamOptions: computed<EntityPickerOption[]>(() =>
      teamCatalog.resources.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.displayName || row.code, row.displayName, row.code))
        .sort(byLabel),
    ),
    teamsPending: teamCatalog.resourcesPending,
  }
}

/**
 * 采集连接器实例目录：控制通道绑定的「实例标识」。
 * 采集健康读面的 `connectorId` 就是连接器实例键（AppHub 侧 `InstanceKey`），`connectorName`
 * 是实例名称，所以这里名称作主文案、实例键作提交值。该读面不回连接器主机 ID，
 * 「连接主机」字段暂无目录可选（后端缺口）。
 */
export function useConnectorInstanceCatalog() {
  const connectorCatalog = useBusinessTelemetryConnectors()

  return {
    connectorInstanceOptions: computed<EntityPickerOption[]>(() =>
      connectorCatalog.connectors.value
        .flatMap((row) => toOption(row.connectorId, row.connectorName, row.sourceSystem))
        .sort(byLabel),
    ),
    connectorsPending: connectorCatalog.connectorsPending,
  }
}

/** 维修工单的人读单号：工单身份是 GUID，列表页统一取末段大写成 `WO-xxxxxxxx`。 */
export function maintenanceWorkOrderNo(workOrderId?: string | null) {
  const id = (workOrderId ?? '').trim()
  return id ? `WO-${id.slice(-8).toUpperCase()}` : ''
}

/**
 * 维护单据目录：保养计划与维修工单。
 * 两者的提交值都是系统 ID（后端按 ID 关联），所以 `value` 用 ID，`label` 用人读单号 /
 * 计划编号，`hint` 标出设备与状态，避免一串 GUID 摆在选项里让人无从分辨。
 */
export function useMaintenanceDocumentCatalog() {
  const planCatalog = useMaintenancePlans({ take: DOCUMENT_CATALOG_TAKE })
  const workOrderCatalog = useMaintenanceWorkOrders({ take: DOCUMENT_CATALOG_TAKE })

  return {
    planOptions: computed<EntityPickerOption[]>(() =>
      planCatalog.plans.value.flatMap((row) =>
        toOption(
          row.planId,
          row.planCode,
          joinHint(row.deviceAssetId, row.nextDueOn ? `下次到期 ${row.nextDueOn}` : ''),
        ),
      ),
    ),
    plansPending: planCatalog.plansPending,
    workOrderOptions: computed<EntityPickerOption[]>(() =>
      workOrderCatalog.workOrders.value.flatMap((row) =>
        toOption(
          row.workOrderId,
          maintenanceWorkOrderNo(row.workOrderId),
          joinHint(row.deviceAssetId, maintenanceWorkOrderStatusLabel(row.status)),
        ),
      ),
    ),
    workOrdersPending: workOrderCatalog.workOrdersPending,
  }
}

const WORK_ORDER_STATUS_LABELS: Record<string, string> = {
  cancelled: '已取消',
  completed: '已完成',
  dispatched: '已派工',
  in_progress: '执行中',
  open: '待处理',
}

function maintenanceWorkOrderStatusLabel(status?: string | null) {
  const key = (status ?? '').trim().toLowerCase()
  if (!key) return ''
  return WORK_ORDER_STATUS_LABELS[key] ?? key
}

/** 备件物料目录：备件需求与工单完工登记的换件行。 */
export function useEquipmentSkuCatalog() {
  const skuCatalog = useBusinessSkus()
  skuCatalog.filters.take = CATALOG_TAKE

  return {
    skuOptions: computed<EntityPickerOption[]>(() =>
      skuCatalog.skus.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, row.baseUomCode))
        .sort(byLabel),
    ),
    skusPending: skuCatalog.skusPending,
    /** 所选物料的基本单位，用来在选完物料后自动带出单位。 */
    baseUomBySku: toBaseUomBySku(skuCatalog.skus),
  }
}
