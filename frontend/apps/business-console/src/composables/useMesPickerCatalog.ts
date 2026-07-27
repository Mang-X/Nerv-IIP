/**
 * 制造执行（MES）域的选择器目录：把主数据 / 工程读面映射成 `EntityPickerOption`。
 *
 * 背景：生产准备检查、工单、报工几张表过去要求手输工厂 / 产线 / 工作中心 / 物料 /
 * 生产版本编码，敲错要到提交才报错。这里按「一域一目录」收敛，各页只声明用哪几组。
 *
 * 口径：`value` 一律是人读业务编码（提交体不变），`label` 是名称，`hint` 放辅助识别信息。
 * 工厂 ▸ 产线 ▸ 工作中心是**父子层级**（后端建模：产线挂 SiteCode，工作中心挂 LineCode），
 * 所以下级候选按上级过滤，页面在上级变更时清空下游已选值。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import { computed } from 'vue'
import { useBusinessMasterDataResources, useBusinessSkus } from './useBusinessMasterData'
import { useEngineeringProductionVersions } from './useProductEngineering'

/** 主数据目录取数上限——单厂的产线 / 工作中心量级在数百条。 */
const CATALOG_TAKE = 500

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

/**
 * 生产范围目录：工厂 ▸ 产线 ▸ 工作中心三级联动。
 * `lineOptions(site)` / `workCenterOptions(site, line)` 返回**已按上级收窄**的候选；
 * 上级为空时给全量（代表「全部」），这样单独选下级也不会被卡死。
 */
export function useProductionScopeCatalog() {
  const siteCatalog = useBusinessMasterDataResources('site')
  const lineCatalog = useBusinessMasterDataResources('production-line')
  const workCenterCatalog = useBusinessMasterDataResources('work-center')
  siteCatalog.filters.take = CATALOG_TAKE
  lineCatalog.filters.take = CATALOG_TAKE
  workCenterCatalog.filters.take = CATALOG_TAKE

  const activeSites = computed(() => siteCatalog.resources.value.filter((r) => r.active !== false))
  const activeLines = computed(() => lineCatalog.resources.value.filter((r) => r.active !== false))
  const activeWorkCenters = computed(() =>
    workCenterCatalog.resources.value.filter((r) => r.active !== false),
  )

  const siteOptions = computed<EntityPickerOption[]>(() =>
    activeSites.value.flatMap((row) => toOption(row.code, row.displayName)).sort(byLabel),
  )

  function linesUnder(siteCode: string) {
    const site = siteCode.trim()
    if (!site) return activeLines.value
    return activeLines.value.filter((row) => row.siteCode === site)
  }

  function lineOptions(siteCode: string): EntityPickerOption[] {
    return linesUnder(siteCode)
      .flatMap((row) => toOption(row.code, row.displayName, row.siteCode))
      .sort(byLabel)
  }

  function workCenterOptions(siteCode: string, lineCode: string): EntityPickerOption[] {
    const line = lineCode.trim()
    let rows = activeWorkCenters.value
    if (line) {
      rows = rows.filter((row) => row.lineCode === line)
    } else if (siteCode.trim()) {
      // 只选了工厂：工作中心通过所属产线归到该工厂（也兼容直接挂 plantCode 的行）。
      const lineCodes = new Set(
        linesUnder(siteCode)
          .map((row) => row.code)
          .filter((code): code is string => !!code),
      )
      rows = rows.filter(
        (row) =>
          row.plantCode === siteCode.trim() || (!!row.lineCode && lineCodes.has(row.lineCode)),
      )
    }
    return rows.flatMap((row) => toOption(row.code, row.displayName, row.lineCode)).sort(byLabel)
  }

  return {
    siteOptions,
    sitesPending: siteCatalog.resourcesPending,
    lineOptions,
    linesPending: lineCatalog.resourcesPending,
    workCenterOptions,
    workCentersPending: workCenterCatalog.resourcesPending,
  }
}

/**
 * 物料 ▸ 生产版本目录：生产版本从属于物料（后端 ProductionVersion 挂 SkuCode），
 * 所以 `productionVersionOptions(skuCode)` 只列该物料的版本。
 *
 * 生产版本没有人读业务编码——后端只暴露 `productionVersionId`（GUID）+ SkuCode + 生效区间，
 * 所以选项 `value` 用 id、`label` 用「物料 · 生效日」，让用户读到的是业务口径而不是裸 GUID。
 */
export function useMesMaterialVersionCatalog() {
  const skuCatalog = useBusinessSkus()
  const versionCatalog = useEngineeringProductionVersions()
  skuCatalog.filters.take = CATALOG_TAKE
  versionCatalog.filters.take = CATALOG_TAKE

  const skuOptions = computed<EntityPickerOption[]>(() =>
    skuCatalog.skus.value
      .filter((row) => row.active !== false)
      .flatMap((row) => toOption(row.code, row.displayName, row.baseUomCode))
      .sort(byLabel),
  )

  const skuNameByCode = computed(() => {
    const map = new Map<string, string>()
    for (const row of skuCatalog.skus.value) {
      const code = row.code?.trim()
      if (code) map.set(code, row.displayName?.trim() || code)
    }
    return map
  })

  function productionVersionOptions(skuCode: string): EntityPickerOption[] {
    const sku = skuCode.trim()
    return versionCatalog.productionVersions.value
      .filter((row) => !!row.productionVersionId && (!sku || row.skuCode === sku))
      .map<EntityPickerOption>((row) => {
        const owner = row.skuCode ? (skuNameByCode.value.get(row.skuCode) ?? row.skuCode) : '未知物料'
        const from = row.validFrom ? row.validFrom.slice(0, 10) : ''
        return {
          value: row.productionVersionId as string,
          label: from ? `${owner} · 生效 ${from}` : owner,
          ...(row.isDefault ? { hint: '默认版本' } : {}),
        }
      })
  }

  return {
    skuOptions,
    skusPending: skuCatalog.skusPending,
    productionVersionOptions,
    productionVersionsPending: versionCatalog.productionVersionsPending,
  }
}
