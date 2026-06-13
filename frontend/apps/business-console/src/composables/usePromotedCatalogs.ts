/**
 * 「字典升主数据」过渡期桩 composable（⏳ #397）。
 *
 * #397 把若干本不该放数据字典的对象升为主数据：产品/物料分类（分类树）、
 * 质量原因（分组目录）、技能（分组目录）。后端 facade 尚未交付，这里先给
 * **空数据 + 抛错的保存**，让前端把页面（IA / 列表 / 表单）搭起来，**不塞假数据**。
 *
 * 交付后逐个：把 import 换成生成层 list/create/update/archive options，用
 * useQuery/useMutation 替换空 ref / notReady()，并把页面横幅的 backendReady 置真；
 * 页面模板与绑定无需改动。届时本文件可整体拆除/替换。
 *
 * 三个实体的身份字段统一叫 `id`（避开黄金标准契约对 `operationId` 等开发词的子串误伤；
 * 交付后映射后端真实主键到此字段）。
 */
import { useBusinessContextStore } from '@/stores/businessContext'
import { computed, reactive, ref } from 'vue'

const DEFAULT_TAKE = 100

export interface PromotedListFilters {
  organizationId: string
  environmentId: string
  search?: string
  skip: number
  take: number
}

// ── 产品/物料分类（分类树）────────────────────────────────────────
export interface ProductCategoryItem {
  id?: string
  categoryCode?: string
  categoryName?: string
  parentCode?: string | null
  parentName?: string | null
  description?: string | null
  enabled?: boolean
  status?: string
}
export interface CreateProductCategoryRequest {
  organizationId: string
  environmentId: string
  categoryName: string
  parentCode?: string | null
  description?: string | null
  enabled?: boolean
}
export type UpdateProductCategoryRequest = Omit<CreateProductCategoryRequest, 'organizationId' | 'environmentId'>

// ── 质量原因（分组目录）──────────────────────────────────────────
export interface QualityReasonItem {
  id?: string
  reasonCode?: string
  reasonName?: string
  groupName?: string | null
  severity?: string | null
  defaultDisposition?: string | null
  enabled?: boolean
  status?: string
}
export interface CreateQualityReasonRequest {
  organizationId: string
  environmentId: string
  reasonName: string
  groupName?: string | null
  severity?: string | null
  defaultDisposition?: string | null
  enabled?: boolean
}
export type UpdateQualityReasonRequest = Omit<CreateQualityReasonRequest, 'organizationId' | 'environmentId'>

// ── 技能（分组目录）──────────────────────────────────────────────
export interface SkillCatalogItem {
  id?: string
  skillCode?: string
  skillName?: string
  groupName?: string | null
  requiresCertification?: boolean
  validityMonths?: number | null
  description?: string | null
  enabled?: boolean
  status?: string
}
export interface CreateSkillCatalogRequest {
  organizationId: string
  environmentId: string
  skillName: string
  groupName?: string | null
  requiresCertification?: boolean
  validityMonths?: number | null
  description?: string | null
  enabled?: boolean
}
export type UpdateSkillCatalogRequest = Omit<CreateSkillCatalogRequest, 'organizationId' | 'environmentId'>

// ── 桩工厂：统一构造一个「空数据 + 抛错保存」的 composable ────────────
function createPendingCatalogStub<TItem>(notReadyMessage: string) {
  const context = useBusinessContextStore()
  const filters = reactive<PromotedListFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    search: undefined,
    skip: 0,
    take: DEFAULT_TAKE,
  })
  // TODO(#397) 后端交付后：用 list...QueryOptions 的 useQuery 替换以下空桩。
  const items = ref<TItem[]>([])
  const pending = ref(false)
  const error = ref<unknown>(null)
  const total = ref(0)
  const noPending = ref(false)
  const notReady = (): Promise<never> => Promise.reject(new Error(notReadyMessage))

  return { filters, items, pending, error, total, noPending, notReady }
}

const CATEGORY_NOT_READY = '产品分类主数据尚未交付（#397），当前为页面预览，保存暂不可用。'
const REASON_NOT_READY = '质量原因目录尚未交付（#397），当前为页面预览，保存暂不可用。'
const SKILL_NOT_READY = '技能目录尚未交付（#397），当前为页面预览，保存暂不可用。'

export function useProductCategories() {
  const s = createPendingCatalogStub<ProductCategoryItem>(CATEGORY_NOT_READY)
  return {
    backendReady: false as boolean,
    filters: s.filters,
    categories: computed<ProductCategoryItem[]>(() => s.items.value),
    categoriesError: s.error,
    categoriesPending: s.pending,
    categoriesTotal: computed(() => s.total.value),
    refresh: async () => {},
    createCategory: (_body: CreateProductCategoryRequest) => s.notReady(),
    createPending: s.noPending,
    updateCategory: (_id: string, _body: UpdateProductCategoryRequest) => s.notReady(),
    updatePending: s.noPending,
    archiveCategory: (_id: string, _reason: string) => s.notReady(),
    archivePending: s.noPending,
  }
}

export function useQualityReasonCodes() {
  const s = createPendingCatalogStub<QualityReasonItem>(REASON_NOT_READY)
  return {
    backendReady: false as boolean,
    filters: s.filters,
    reasons: computed<QualityReasonItem[]>(() => s.items.value),
    reasonsError: s.error,
    reasonsPending: s.pending,
    reasonsTotal: computed(() => s.total.value),
    refresh: async () => {},
    createReason: (_body: CreateQualityReasonRequest) => s.notReady(),
    createPending: s.noPending,
    updateReason: (_id: string, _body: UpdateQualityReasonRequest) => s.notReady(),
    updatePending: s.noPending,
    archiveReason: (_id: string, _reason: string) => s.notReady(),
    archivePending: s.noPending,
  }
}

export function useSkillCatalog() {
  const s = createPendingCatalogStub<SkillCatalogItem>(SKILL_NOT_READY)
  return {
    backendReady: false as boolean,
    filters: s.filters,
    skills: computed<SkillCatalogItem[]>(() => s.items.value),
    skillsError: s.error,
    skillsPending: s.pending,
    skillsTotal: computed(() => s.total.value),
    refresh: async () => {},
    createSkill: (_body: CreateSkillCatalogRequest) => s.notReady(),
    createPending: s.noPending,
    updateSkill: (_id: string, _body: UpdateSkillCatalogRequest) => s.notReady(),
    updatePending: s.noPending,
    archiveSkill: (_id: string, _reason: string) => s.notReady(),
    archivePending: s.noPending,
  }
}
