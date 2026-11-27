import {
  getBusinessConsoleEngineeringBomDiffQueryOptions,
  getBusinessConsoleEngineeringBomExplosionQueryOptions,
  getBusinessConsoleEngineeringBomWhereUsedQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomExplosionQueryOptions,
  getBusinessConsoleEngineeringManufacturingBomWhereUsedQueryOptions,
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringItemsQueryOptions,
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  type BusinessConsoleBomExplosionResponse,
  type BusinessConsoleBomDiffResponse,
  type BusinessConsoleBomWhereUsedResponse,
  type BusinessConsoleEngineeringItemRevisionItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed, reactive, ref, shallowRef, toValue, type MaybeRefOrGetter } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import {
  DEFAULT_TAKE,
  optionalQuery,
  runQueryOption,
  unwrapDetail,
  unwrapItems,
} from './engineeringShared'
import { usePublishedEboms, usePublishedMboms } from './useEngineeringBoms'

export interface BomExplosionInput {
  code: string
  effectiveDate: string
  lotSize?: number
  bomCode?: string
  revision?: string
}

export interface BomWhereUsedInput {
  componentCode: string
  effectiveDate: string
}

export interface BomDiffInput {
  bomKind: 'engineering' | 'manufacturing'
  fromBomCode: string
  fromRevision: string
  toBomCode: string
  toRevision: string
}

export type AnalysisRequest = 'explosion' | 'where-used' | 'diff'

function analysisRequest<T>() {
  const result = shallowRef<T>()
  const pending = ref(false)
  const error = shallowRef<unknown>()
  let generation = 0

  async function run(work: () => Promise<T | undefined>) {
    const request = ++generation
    pending.value = true
    error.value = undefined
    result.value = undefined
    try {
      const response = await work()
      if (request === generation) result.value = response
    } catch (reason) {
      if (request === generation) error.value = reason
    } finally {
      if (request === generation) pending.value = false
    }
  }

  function reset() {
    generation += 1
    result.value = undefined
    pending.value = false
    error.value = undefined
  }

  return { result, pending, error, run, reset }
}

export function useBomAnalysis() {
  const context = useBusinessContextStore()
  const explosion = analysisRequest<BusinessConsoleBomExplosionResponse>()
  const diff = analysisRequest<BusinessConsoleBomDiffResponse>()
  const whereUsed = analysisRequest<BusinessConsoleBomWhereUsedResponse>()
  const requests = { explosion, diff, 'where-used': whereUsed }

  function commonQuery() {
    return {
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }
  }

  return {
    explosion: explosion.result,
    diff: diff.result,
    whereUsed: whereUsed.result,
    pendingFor: (view: AnalysisRequest) => requests[view].pending.value,
    errorFor: (view: AnalysisRequest) => requests[view].error.value,
    reset: (view: AnalysisRequest) => requests[view].reset(),
    loadBomDiff: (input: BomDiffInput) =>
      diff.run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomDiffQueryOptions({
            query: {
              ...commonQuery(),
              bomKind: input.bomKind,
              fromBomCode: input.fromBomCode,
              fromRevision: input.fromRevision,
              toBomCode: input.toBomCode,
              toRevision: input.toRevision,
            },
          }),
        )
        return unwrapDetail<BusinessConsoleBomDiffResponse>(envelope)
      }),
    loadEngineeringExplosion: (input: BomExplosionInput) =>
      explosion.run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomExplosionQueryOptions({
            query: {
              ...commonQuery(),
              itemCode: input.code,
              effectiveDate: input.effectiveDate,
              ...optionalQuery('lotSize', input.lotSize),
              ...optionalQuery('bomCode', input.bomCode),
              ...optionalQuery('revision', input.revision),
            },
          }),
        )
        return unwrapDetail<BusinessConsoleBomExplosionResponse>(envelope)
      }),
    loadManufacturingExplosion: (input: BomExplosionInput) =>
      explosion.run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringManufacturingBomExplosionQueryOptions({
            query: {
              ...commonQuery(),
              skuCode: input.code,
              effectiveDate: input.effectiveDate,
              ...optionalQuery('lotSize', input.lotSize),
              ...optionalQuery('bomCode', input.bomCode),
              ...optionalQuery('revision', input.revision),
            },
          }),
        )
        return unwrapDetail<BusinessConsoleBomExplosionResponse>(envelope)
      }),
    loadEngineeringWhereUsed: (input: BomWhereUsedInput) =>
      whereUsed.run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringBomWhereUsedQueryOptions({
            query: {
              ...commonQuery(),
              componentCode: input.componentCode,
              effectiveDate: input.effectiveDate,
            },
          }),
        )
        return unwrapDetail<BusinessConsoleBomWhereUsedResponse>(envelope)
      }),
    loadManufacturingWhereUsed: (input: BomWhereUsedInput) =>
      whereUsed.run(async () => {
        const envelope = await runQueryOption(
          getBusinessConsoleEngineeringManufacturingBomWhereUsedQueryOptions({
            query: {
              ...commonQuery(),
              componentCode: input.componentCode,
              effectiveDate: input.effectiveDate,
            },
          }),
        )
        return unwrapDetail<BusinessConsoleBomWhereUsedResponse>(envelope)
      }),
  }
}

export interface BomCatalogEntry {
  /** 人读业务编码（EBOM 侧 itemCode，MBOM 侧 skuCode）。 */
  code: string
  name: string
  hint?: string
}

const ENGINEERING_ITEM_STATUS_LABELS: Record<string, string> = {
  draft: '草稿',
  published: '已发布',
  archived: '已归档',
}

/** 工程物料和基础数据 SKU 共享编码的选择目录。 */
export function useBomItemCatalog() {
  const context = useBusinessContextStore()
  const engineeringRevisions = shallowRef<BusinessConsoleEngineeringItemRevisionItem[]>([])
  const engineeringError = shallowRef<unknown>()
  const skuError = shallowRef<unknown>()
  const engineeringEntries = computed(() => dedupeEngineeringItems(engineeringRevisions.value))
  const skuEntries = shallowRef<BomCatalogEntry[]>([])
  const pending = ref(false)
  const itemEntries = computed(() => {
    const byCode = new Map(skuEntries.value.map((entry) => [entry.code, entry]))
    for (const entry of engineeringEntries.value) byCode.set(entry.code, entry)
    return [...byCode.values()]
  })

  function dedupeEngineeringItems(items: BusinessConsoleEngineeringItemRevisionItem[]) {
    const byCode = new Map<string, BusinessConsoleEngineeringItemRevisionItem>()
    for (const item of items) {
      if (!item.itemCode) continue
      const current = byCode.get(item.itemCode)
      const published = (item.status ?? '').toLowerCase() === 'published'
      const currentPublished = (current?.status ?? '').toLowerCase() === 'published'
      if (!current || (published && !currentPublished)) byCode.set(item.itemCode, item)
    }
    return [...byCode.values()].map<BomCatalogEntry>((item) => ({
      code: item.itemCode as string,
      name: item.name ?? (item.itemCode as string),
      hint: ENGINEERING_ITEM_STATUS_LABELS[(item.status ?? '').toLowerCase()],
    }))
  }

  async function loadSkus(query: { organizationId: string; environmentId: string; take: number }) {
    const byCode = new Map<string, BomCatalogEntry>()
    let skip = 0
    while (true) {
      const envelope: BusinessConsoleResourceListEnvelope = await runQueryOption(
        listBusinessConsoleSkusQueryOptions({ query: { ...query, skip } }),
      )
      const resources = envelope.success ? (envelope.data?.resources ?? []) : []
      for (const sku of resources) {
        if (sku.code) byCode.set(sku.code, { code: sku.code, name: sku.displayName ?? sku.code })
      }
      skip += resources.length
      if (resources.length === 0 || skip >= (envelope.data?.total ?? 0)) break
    }
    return [...byCode.values()]
  }

  async function loadEngineeringItems(query: {
    organizationId: string
    environmentId: string
    take: number
  }) {
    const revisions: BusinessConsoleEngineeringItemRevisionItem[] = []
    while (true) {
      const envelope = await runQueryOption(
        listBusinessConsoleEngineeringItemsQueryOptions({
          query: { ...query, skip: revisions.length },
        }),
      )
      const batch = unwrapItems<BusinessConsoleEngineeringItemRevisionItem>(envelope)
      revisions.push(...batch)
      if (!batch.length || revisions.length >= (envelope.data?.total ?? 0)) return revisions
    }
  }

  async function load() {
    if (pending.value) return
    pending.value = true
    engineeringError.value = undefined
    skuError.value = undefined
    const query = {
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      take: DEFAULT_TAKE,
    }
    try {
      const [items, skus] = await Promise.allSettled([loadEngineeringItems(query), loadSkus(query)])
      if (items.status === 'fulfilled') engineeringRevisions.value = items.value
      else engineeringError.value = items.reason
      if (skus.status === 'fulfilled') skuEntries.value = skus.value
      else skuError.value = skus.reason
    } finally {
      pending.value = false
    }
  }

  return {
    engineeringEntries,
    engineeringRevisions,
    engineeringError,
    skuEntries,
    skuError,
    itemEntries,
    pending,
    load,
  }
}

export type BomVersionKind = 'engineering' | 'manufacturing'

export interface BomVersionPickerEntry {
  bomCode: string
  revision: string
  /** EBOM 为父项物料编码，MBOM 为产出 SKU 编码。 */
  ownerCode?: string
  effectiveDate?: string
}

export interface BomVersionPickerOption {
  value: string
  label: string
  hint?: string
}

/** 已选值不在目录里（深链 / 目录截断）时兜住它，避免选择器显示成未选。 */
function keepCurrentOption(options: BomVersionPickerOption[], current: string) {
  const trimmed = current.trim()
  if (trimmed && !options.some((option) => option.value === trimmed)) {
    return [{ value: trimmed, label: trimmed }, ...options]
  }
  return options
}

/**
 * BOM 版本选择目录（已发布 EBOM + MBOM），供「指定 BOM 编码 + 指定修订」这类**联动**字段使用：
 * 先选 BOM 编码，修订选项只列该 BOM 已发布的修订。BOM 分析页的对比 / 指定版本三处复用同一份目录。
 */
export function useBomVersionPickerCatalog() {
  const { eboms, ebomsPending } = usePublishedEboms()
  const { mboms, mbomsPending } = usePublishedMboms()

  const engineeringEntries = computed<BomVersionPickerEntry[]>(() =>
    eboms.value
      .filter((bom) => !!bom.bomCode && !!bom.revision)
      .map((bom) => ({
        bomCode: bom.bomCode as string,
        revision: bom.revision as string,
        ownerCode: bom.parentItemCode ?? undefined,
        effectiveDate: bom.effectiveDate ?? undefined,
      })),
  )
  const manufacturingEntries = computed<BomVersionPickerEntry[]>(() =>
    mboms.value
      .filter((bom) => !!bom.bomCode && !!bom.revision)
      .map((bom) => ({
        bomCode: bom.bomCode as string,
        revision: bom.revision as string,
        ownerCode: bom.skuCode ?? undefined,
        effectiveDate: bom.effectiveDate ?? undefined,
      })),
  )

  const pending = computed(() => ebomsPending.value || mbomsPending.value)

  function entriesFor(kind: BomVersionKind) {
    return kind === 'engineering' ? engineeringEntries.value : manufacturingEntries.value
  }

  /** BOM 编码选项（同一编码多修订时只出现一次，hint 给归属物料 / SKU）。 */
  function bomCodeOptions(kind: BomVersionKind, current = ''): BomVersionPickerOption[] {
    const byCode = new Map<string, BomVersionPickerOption>()
    for (const entry of entriesFor(kind)) {
      if (byCode.has(entry.bomCode)) continue
      byCode.set(entry.bomCode, {
        value: entry.bomCode,
        label: entry.bomCode,
        hint: entry.ownerCode,
      })
    }
    return keepCurrentOption([...byCode.values()], current)
  }

  /** 修订选项：跟随所选 BOM 编码；未选 BOM 时不给候选（避免跨 BOM 的无效组合）。 */
  function revisionOptions(
    kind: BomVersionKind,
    bomCode: string,
    current = '',
  ): BomVersionPickerOption[] {
    const code = bomCode.trim()
    if (!code) return keepCurrentOption([], current)
    const options = entriesFor(kind)
      .filter((entry) => entry.bomCode === code)
      .map<BomVersionPickerOption>((entry) => ({
        value: entry.revision,
        label: entry.revision,
        hint: entry.effectiveDate ? `生效 ${entry.effectiveDate.slice(0, 10)}` : undefined,
      }))
    return keepCurrentOption(options, current)
  }

  return {
    engineeringEntries,
    manufacturingEntries,
    bomVersionsPending: pending,
    bomCodeOptions,
    revisionOptions,
  }
}

/**
 * 某物料下已存在的 BOM 修订号（含草稿 / 已归档），供「发布新版本」的修订号输入使用：
 * 修订号是**用户新建**的值，不能做成只读选择器，所以给「已占用」建议列表 + 重复校验，
 * 让用户一眼看到哪些号已经用掉。EBOM 按父项物料、MBOM 按产出 SKU 拉取。
 */
export function useBomRevisionSuggestions(
  kind: BomVersionKind,
  ownerCode: MaybeRefOrGetter<string | undefined>,
) {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )

  const query = useQuery(() => {
    const code = (toValue(ownerCode) ?? '').trim()
    const options =
      kind === 'engineering'
        ? listBusinessConsoleEngineeringBomsQueryOptions({
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              parentItemCode: code,
              skip: 0,
              take: DEFAULT_TAKE,
            },
          })
        : listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              skuCode: code,
              skip: 0,
              take: DEFAULT_TAKE,
            },
          })
    return {
      ...withBusinessContextEnabled(options, filters),
      // 没选物料就别发请求（否则会拉回全租户的 BOM，建议列表毫无意义）。
      enabled: hasBusinessContext(filters) && code.length > 0,
    }
  })

  type RevisionRow = { revision?: string; status?: string }
  const takenRevisions = computed<BomVersionPickerOption[]>(() => {
    // EBOM / MBOM 两种 envelope 只用到 revision + status 两个共有字段，按最小结构解包。
    const envelope = query.data.value as
      | { success?: boolean; data?: { items?: RevisionRow[] } | null }
      | undefined
    const items = unwrapItems<RevisionRow>(envelope)
    const byRevision = new Map<string, BomVersionPickerOption>()
    for (const item of items) {
      const revision = (item.revision ?? '').trim()
      if (!revision || byRevision.has(revision)) continue
      byRevision.set(revision, {
        value: revision,
        label: revision,
        hint: ENGINEERING_ITEM_STATUS_LABELS[(item.status ?? '').toLowerCase()] ?? item.status,
      })
    }
    return [...byRevision.values()]
  })

  function isTaken(revision: string) {
    const trimmed = revision.trim()
    return !!trimmed && takenRevisions.value.some((option) => option.value === trimmed)
  }

  return { takenRevisions, takenRevisionsPending: query.isLoading, isTaken }
}
