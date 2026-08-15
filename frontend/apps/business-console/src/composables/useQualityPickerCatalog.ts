/**
 * 质量域的选择器目录：把主数据 / 质量读面映射成选择器选项。
 *
 * 背景：待检工作台、检验记录、质量分析、原因码目录几个页面过去都要求手输 SKU、检验方案、
 * 特性、单位、原因码——敲错要等提交才报错，且分析页的特性一旦敲成计划外编码就查不到数据。
 * 这里按「一域一目录」收敛，各页只声明用哪几组目录。
 *
 * 口径：
 * - 实体（物料 / 单位 / 检验方案 / 特性）→ `EntityPickerOption`，`label` 是人读名称、
 *   `hint` 放辅助识别信息（编码 / 规格 / 物料）。
 * - 原因码是字典值 → `SearchSelectOption`。缺陷原因存 **原因编码**（质量服务的 CAPA 归集按
 *   `ReasonCode == DefectReason` 匹配，存名称会断掉自动归集）；处置原因是 500 字的处置结论
 *   叙述字段，存 **原因名称** 才能在记录详情里直接读懂。
 */
import type { EntityPickerOption, SearchSelectOption } from '@nerv-iip/ui'
import type { BusinessConsoleInspectionPlanCharacteristicItem } from '@nerv-iip/api-client'
import { computed, type MaybeRefOrGetter, toValue } from 'vue'
import { useBusinessSkus, useBusinessUoms, useBusinessWorkers } from './useBusinessMasterData'
import { useQualityReasonCodes } from './usePromotedCatalogs'
import {
  useQualityInspectionPlanCharacteristics,
  useQualityInspectionPlans,
  useQualityNcrs,
} from './useBusinessQuality'
import { useBusinessContextStore } from '@/stores/businessContext'

/** 目录取数上限——单个工厂的物料 / 检验方案 / 原因码量级在数百条。 */
const CATALOG_TAKE = 500
const WORKER_CATALOG_TAKE = 200

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

function byLabel(a: { label: string }, b: { label: string }) {
  return a.label.localeCompare(b.label, 'zh-Hans-CN')
}

function joinHint(...parts: (string | null | undefined)[]) {
  return parts
    .map((part) => part?.trim())
    .filter(Boolean)
    .join(' · ')
}

/**
 * 质量读面的跨单据 / 人员目录。
 *
 * CAPA 等只读 DTO 仍以稳定 id 表达关联；页面只允许把 id 当查表键，不能把它当展示兜底。
 * 员工目录接口单页上限为 200，当前单工厂演示人员规模远低于该上限。
 */
export function useQualityReadFaceCatalog() {
  const { ncrs } = useQualityNcrs({ take: CATALOG_TAKE })
  const { workers } = useBusinessWorkers({ includeDisabled: true, pageSize: WORKER_CATALOG_TAKE })

  return {
    /** NCR 聚合标识 → NCR 单号。 */
    ncrCodeById: computed(() => {
      const map = new Map<string, string>()
      for (const ncr of ncrs.value) {
        const id = ncr.id?.trim()
        const code = ncr.code?.trim()
        if (id && code) map.set(id, code)
      }
      return map
    }),
    /** IAM userId → 姓名 · 工号；缺任一项时只显示已有的人读字段。 */
    workerLabelById: computed(() => {
      const map = new Map<string, string>()
      for (const worker of workers.value) {
        const userId = worker.userId?.trim()
        const name = worker.displayName?.trim()
        const employeeNo = worker.employeeNo?.trim()
        const label = name && employeeNo ? `${name} · ${employeeNo}` : name || employeeNo
        if (userId && label) map.set(userId, label)
      }
      return map
    }),
  }
}

/** 物料目录：待检工作台筛选、检验记录的检验对象、质量分析的 SPC 范围都用它。 */
export function useQualitySkuCatalog() {
  const skuCatalog = useBusinessSkus()
  skuCatalog.filters.take = CATALOG_TAKE

  return {
    skuOptions: computed<EntityPickerOption[]>(() =>
      skuCatalog.skus.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, joinHint(row.code, row.baseUomCode)))
        .sort(byLabel),
    ),
    skusPending: skuCatalog.skusPending,
    /** 编码 → 名称，供表格补名称列。 */
    skuNameByCode: computed(() => {
      const map = new Map<string, string>()
      for (const row of skuCatalog.skus.value) {
        const code = row.code?.trim()
        const name = row.displayName?.trim()
        if (code && name) map.set(code, name)
      }
      return map
    }),
  }
}

/** 计量单位目录：检验特性行的实测值单位。 */
export function useQualityUomCatalog() {
  const uomCatalog = useBusinessUoms()
  uomCatalog.filters.take = CATALOG_TAKE

  return {
    uomOptions: computed<EntityPickerOption[]>(() =>
      uomCatalog.uoms.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, row.code))
        .sort(byLabel),
    ),
    uomsPending: uomCatalog.uomsPending,
  }
}

/** 检验方案目录：检验记录挂哪个方案、质量分析先定方案再定特性。 */
export function useQualityInspectionPlanCatalog() {
  const { inspectionPlans, inspectionPlansPending } = useQualityInspectionPlans({
    take: CATALOG_TAKE,
  })

  return {
    inspectionPlans,
    inspectionPlansPending,
    /** value 用方案的系统标识（创建检验记录、查特性清单都按它定位），label 显示人读方案号。 */
    inspectionPlanOptions: computed<EntityPickerOption[]>(() =>
      inspectionPlans.value.flatMap((plan) => {
        const id = plan.id?.trim()
        if (!id) return []
        const label = plan.code?.trim() || id
        const hint = joinHint(plan.skuCode, plan.category)
        return [{ value: id, label, ...(hint ? { hint } : {}) }]
      }),
    ),
    /** 方案标识 → 人读方案号，供只读回显。 */
    inspectionPlanCodeById: computed(() => {
      const map = new Map<string, string>()
      for (const plan of inspectionPlans.value) {
        const id = plan.id?.trim()
        const code = plan.code?.trim()
        if (id && code) map.set(id, code)
      }
      return map
    }),
  }
}

function characteristicSpecification(item: BusinessConsoleInspectionPlanCharacteristicItem) {
  const unit = item.unitCode?.trim() ? ` ${item.unitCode.trim()}` : ''
  if (item.lowerSpecLimit != null || item.upperSpecLimit != null) {
    return `${item.lowerSpecLimit ?? '—'}–${item.upperSpecLimit ?? '—'}${unit}`
  }
  return item.nominalValue == null ? '' : `目标 ${item.nominalValue}${unit}`
}

/**
 * 某个检验方案的特性目录。
 *
 * 特性没有独立的全域读面，只能挂在检验方案下（`inspectionPlanId` 是路径参数），
 * 所以调用方必须先定方案——这也是页面上「先选方案再选特性」联动的由来。
 */
export function useQualityCharacteristicCatalog(inspectionPlanId: MaybeRefOrGetter<string>) {
  const context = useBusinessContextStore()
  const { planCharacteristics, planCharacteristicsError, planCharacteristicsPending } =
    useQualityInspectionPlanCharacteristics(() => ({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      inspectionPlanId: toValue(inspectionPlanId).trim(),
    }))

  return {
    planCharacteristics,
    planCharacteristicsError,
    planCharacteristicsPending,
    characteristicOptions: computed<EntityPickerOption[]>(() =>
      planCharacteristics.value.flatMap((item) => {
        const code = item.characteristicCode?.trim()
        if (!code) return []
        const hint = joinHint(code, characteristicSpecification(item))
        return [{ value: code, label: item.name?.trim() || code, ...(hint ? { hint } : {}) }]
      }),
    ),
  }
}

/** 质量原因目录：缺陷原因、处置原因、原因组建议共用一次取数。 */
export function useQualityReasonCatalog() {
  const catalog = useQualityReasonCodes()
  catalog.filters.take = CATALOG_TAKE
  catalog.filters.enabled = true

  const activeReasons = computed(() => catalog.reasons.value.filter((row) => row.enabled !== false))

  return {
    reasonsPending: catalog.reasonsPending,
    /** 缺陷原因：value 存原因编码，与质量服务 CAPA 归集口径一致。 */
    defectReasonOptions: computed<SearchSelectOption[]>(() =>
      activeReasons.value
        .flatMap((row) => {
          const value = row.reasonCode?.trim()
          if (!value) return []
          const hint = joinHint(row.groupName, value)
          return [{ value, label: row.reasonName?.trim() || value, ...(hint ? { hint } : {}) }]
        })
        .sort(byLabel),
    ),
    /** 处置原因：字段是处置结论叙述（记录详情直接展示），所以存人读原因名称。 */
    dispositionReasonOptions: computed<SearchSelectOption[]>(() =>
      activeReasons.value
        .flatMap((row) => {
          const label = row.reasonName?.trim()
          if (!label) return []
          const hint = joinHint(row.groupName, row.reasonCode)
          return [{ value: label, label, ...(hint ? { hint } : {}) }]
        })
        .sort(byLabel),
    ),
    /** 原因组：目录自身的维护页允许新建新组，所以给的是建议而不是封闭选项。 */
    reasonGroupSuggestions: computed(() => {
      const groups = new Set<string>()
      for (const row of catalog.reasons.value) {
        const group = row.groupName?.trim()
        if (group) groups.add(group)
      }
      return [...groups]
        .sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'))
        .map((group) => ({ value: group, label: group }))
    }),
  }
}

/**
 * 质量处置类型。
 *
 * 码值以**后端校验器为准**：`QualityReasonCommands.cs` 明确要求 DefaultDisposition
 * 只能是 rework / scrap / return-to-supplier / conditional-release，或者留空。
 * （早前这里写过 `use-as-is`，后端并不接受，提交会被直接打回；`conditional-release`
 * 才是「让步接收」对应的码值。改这份常量前先回后端核一遍校验器。）
 */
export const QUALITY_DISPOSITION_OPTIONS = [
  { value: 'rework', label: '返工' },
  { value: 'conditional-release', label: '让步接收' },
  { value: 'scrap', label: '报废' },
  { value: 'return-to-supplier', label: '退供应商' },
] as const

/**
 * 历史数据里可能存着常量表覆盖不到的自由文本（该字段过去是手输的），
 * 渲染时如实标注出来，不静默丢弃、也不假装它是合法码值。
 */
export function qualityDispositionLabel(value?: string | null) {
  const trimmed = value?.trim()
  if (!trimmed) return ''
  const known = QUALITY_DISPOSITION_OPTIONS.find((option) => option.value === trimmed)
  return known ? known.label : `未知处置：${trimmed}`
}
