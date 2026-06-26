<script setup lang="ts">
import type {
  BusinessConsoleCreateProductionVersionRequest,
  BusinessConsoleProductionVersionItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessSkus } from '@/composables/useBusinessMasterData'
import {
  useEngineeringProductionVersions,
  usePublishedMboms,
  usePublishedRoutings,
  useProductionVersionResolve,
} from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialogPro,
  AlertDialogProAction,
  AlertDialogProCancel,
  AlertDialogProContent,
  AlertDialogProDescription,
  AlertDialogProFooter,
  AlertDialogProHeader,
  AlertDialogProTitle,
  ButtonPro,
  CardPro,
  CardProContent,
  CardProHeader,
  CardProTitle,
  CheckboxPro,
  DataTablePro,
  DatePickerPro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, SearchIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '生产版本' } })

const {
  archiveProductionVersion,
  archivePending,
  createProductionVersion,
  createPending,
  filters,
  productionVersions,
  productionVersionsError,
  productionVersionsPending,
  productionVersionsTotal,
  refresh,
  updateProductionVersion,
  updatePending,
} = useEngineeringProductionVersions()

const { mboms, mbomsPending } = usePublishedMboms()
const { routings, routingsPending } = usePublishedRoutings()
const { skus } = useBusinessSkus()
const { resolve, clear: clearResolve, resolved, resolvePending, resolvedOnce } = useProductionVersionResolve()

const STATUS_FILTER_OPTIONS = [
  { label: '全部状态', value: 'all' },
  { label: '有效', value: 'active' },
  { label: '已归档', value: 'archived' },
]
const statusFilter = ref('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

// Toolbar 搜索绑定到物料编码筛选（filters.skuCode 为可选，用 string 代理避免空串污染查询）。
const skuSearch = computed({
  get: () => filters.skuCode ?? '',
  set: (value: string) => { filters.skuCode = value.trim() ? value : undefined },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

// SKU 码 → 名称映射（复用基础数据 SKU 列表），解析不到则显码。
const skuNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const sku of skus.value) {
    if (sku.code) map.set(sku.code, sku.displayName ?? sku.code)
  }
  return map
})
function skuLabel(code?: string | null) {
  if (!code) return '无'
  return skuNameByCode.value.get(code) ?? code
}

const mbomOptions = computed(() =>
  mboms.value
    .filter((m) => m.bomCode)
    .map((m) => ({
      value: m.bomCode as string,
      label: `${m.bomCode} · ${m.revision ?? '—'}${m.skuCode ? ` · ${skuLabel(m.skuCode)}` : ''}`,
    })),
)
const routingOptions = computed(() =>
  routings.value
    .filter((r) => r.routingCode)
    .map((r) => ({
      value: r.routingCode as string,
      label: `${r.routingCode} · ${r.revision ?? '—'}${r.skuCode ? ` · ${skuLabel(r.skuCode)}` : ''}`,
    })),
)
const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)

function statusTone(status?: string | null) {
  return (status ?? '').toLowerCase() === 'active' ? 'success' : 'neutral'
}
function statusLabel(status?: string | null) {
  return (status ?? '').toLowerCase() === 'active' ? '有效' : '已归档'
}

const activeCount = computed(() => productionVersions.value.filter((v) => v.status?.toLowerCase() === 'active').length)
const defaultCount = computed(() => productionVersions.value.filter((v) => v.isDefault && v.status?.toLowerCase() === 'active').length)

const listErrorMessage = computed(() => formatError(productionVersionsError.value))

function formatLotRange(min?: number | null, max?: number | null) {
  if (min == null && max == null) return '不限'
  return `${min ?? 0} - ${max ?? '不限'}`
}
function formatValidRange(from?: string | null, to?: string | null) {
  return `${formatDate(from)} 至 ${to ? formatDate(to) : '长期'}`
}

const columns: DataTableProColumn<BusinessConsoleProductionVersionItem>[] = [
  { key: 'skuCode', header: '物料', cellClass: 'font-medium' },
  { key: 'binding', header: 'MBOM / 工艺路线' },
  { key: 'valid', header: '有效期', width: 'w-52' },
  { key: 'lotSize', header: '批量区间', width: 'w-32' },
  { key: 'priority', header: '优先级', width: 'w-20', align: 'end' },
  { key: 'isDefault', header: '默认', width: 'w-20' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

// ── 新建 / 编辑表单 ─────────────────────────────────────────────
interface ProductionVersionForm {
  skuCode: string
  mbomVersionId: string
  routingVersionId: string
  validFrom: string | null
  validTo: string | null
  lotSizeMin: string | number
  lotSizeMax: string | number
  priority: string | number
  isDefault: boolean
}

function blankForm(): ProductionVersionForm {
  return {
    skuCode: '',
    mbomVersionId: '',
    routingVersionId: '',
    validFrom: today(),
    validTo: null,
    lotSizeMin: '',
    lotSizeMax: '',
    priority: '0',
    isDefault: false,
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑的 productionVersionId。
const editingId = shallowRef<string | null>(null)
const form = reactive<ProductionVersionForm>(blankForm())

function parseNumber(value: string | number | null | undefined): number | undefined {
  if (value === null || value === undefined) return undefined
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
const lotMin = computed(() => parseNumber(form.lotSizeMin))
const lotMax = computed(() => parseNumber(form.lotSizeMax))

const skuValid = computed(() => form.skuCode.trim().length > 0)
const mbomValid = computed(() => form.mbomVersionId.trim().length > 0)
const routingValid = computed(() => form.routingVersionId.trim().length > 0)
const validFromValid = computed(() => !!form.validFrom)
// from ≤ to（to 可空）
const validRangeValid = computed(() => !form.validFrom || !form.validTo || form.validFrom <= form.validTo)
// 批量 min ≤ max（两者皆为非空数值时才校验）
const lotRangeValid = computed(() => lotMin.value == null || lotMax.value == null || lotMin.value <= lotMax.value)

const canSubmit = computed(() =>
  skuValid.value
  && mbomValid.value
  && routingValid.value
  && validFromValid.value
  && validRangeValid.value
  && lotRangeValid.value,
)

const selectorsPending = computed(() => mbomsPending.value || routingsPending.value)
const hasSelectorData = computed(() => mbomOptions.value.length > 0 && routingOptions.value.length > 0)

function openCreate() {
  editingId.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: BusinessConsoleProductionVersionItem) {
  if (!row.productionVersionId) return
  if (row.status?.toLowerCase() === 'archived') return
  editingId.value = row.productionVersionId
  showErrors.value = false
  Object.assign(form, {
    skuCode: row.skuCode ?? '',
    mbomVersionId: row.mbomVersionId ?? '',
    routingVersionId: row.routingVersionId ?? '',
    validFrom: row.validFrom ?? null,
    validTo: row.validTo ?? null,
    lotSizeMin: row.lotSizeMin == null ? '' : String(row.lotSizeMin),
    lotSizeMax: row.lotSizeMax == null ? '' : String(row.lotSizeMax),
    priority: String(row.priority ?? 0),
    isDefault: row.isDefault ?? false,
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const priority = parseNumber(form.priority) ?? 0
  try {
    if (editingId.value) {
      await updateProductionVersion(editingId.value, {
        mbomVersionId: form.mbomVersionId.trim(),
        routingVersionId: form.routingVersionId.trim(),
        validFrom: form.validFrom ?? undefined,
        validTo: form.validTo ?? null,
        lotSizeMin: lotMin.value ?? null,
        lotSizeMax: lotMax.value ?? null,
        priority,
        isDefault: form.isDefault,
      })
      notifySuccess(`物料「${skuLabel(form.skuCode)}」的生产版本已更新。`)
    }
    else {
      const body: BusinessConsoleCreateProductionVersionRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skuCode: form.skuCode.trim(),
        mbomVersionId: form.mbomVersionId.trim(),
        routingVersionId: form.routingVersionId.trim(),
        validFrom: form.validFrom ?? undefined,
        validTo: form.validTo ?? null,
        lotSizeMin: lotMin.value ?? null,
        lotSizeMax: lotMax.value ?? null,
        priority,
        isDefault: form.isDefault,
      }
      await createProductionVersion(body)
      notifySuccess(`已为物料「${skuLabel(form.skuCode)}」创建生产版本。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingId.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 归档 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<BusinessConsoleProductionVersionItem | null>(null)
const archiveReason = ref('')
function openArchive(row: BusinessConsoleProductionVersionItem) {
  if (!row.productionVersionId || row.status?.toLowerCase() === 'archived') return
  archiveTarget.value = row
  archiveReason.value = ''
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.productionVersionId) return
  const reason = archiveReason.value.trim() || '不再用于排产'
  try {
    await archiveProductionVersion(target.productionVersionId, reason)
    notifySuccess(`物料「${skuLabel(target.skuCode)}」的生产版本已归档。`)
    archiveOpen.value = false
    archiveTarget.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── resolve 解析卡 ──────────────────────────────────────────────
const resolveForm = reactive({
  skuCode: '',
  effectiveDate: today() as string | null,
  lotSize: '100',
})
const canResolve = computed(() =>
  resolveForm.skuCode.trim().length > 0
  && !!resolveForm.effectiveDate
  && (parseNumber(resolveForm.lotSize) ?? -1) >= 0,
)
async function runResolve() {
  if (!canResolve.value) return
  try {
    await resolve({
      skuCode: resolveForm.skuCode.trim(),
      effectiveDate: resolveForm.effectiveDate ?? '',
      lotSize: parseNumber(resolveForm.lotSize) ?? 0,
    })
  }
  catch (error) {
    notifyError(error)
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="生产版本"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${productionVersionsTotal} 个版本`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="productionVersionsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建生产版本
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>{{ editingId ? '编辑生产版本' : '新建生产版本' }}</DialogProTitle>
              <DialogProDescription>
                把物料绑定到一套已发布的 MBOM 与工艺路线，并约定有效期、批量区间和优先级。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项，并确保有效期起止、批量区间合法（已标红）。
              </p>
              <p
                v-if="!selectorsPending && !hasSelectorData"
                class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
                role="alert"
              >
                当前没有已发布的 MBOM 或工艺路线可绑定。请先在 MBOM / 工艺路线页发布版本，再回来创建生产版本。
              </p>

              <FormSectionTitle>绑定对象</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !skuValid">
                  <FieldProLabel for="pv-sku">物料 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.skuCode" :disabled="!!editingId">
                    <SelectProTrigger id="pv-sku"><SelectProValue placeholder="选择物料" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>来自基础数据 SKU。{{ editingId ? '编辑时物料不可更改。' : '缺少物料？去基础数据维护。' }}</FieldProDescription>
                </FieldPro>
                <FieldPro class="self-start">
                  <FieldProLabel>设为默认</FieldProLabel>
                  <label
                    for="pv-default"
                    class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm"
                  >
                    <span>同一物料生效期内的默认版本</span>
                    <CheckboxPro id="pv-default" v-model:checked="form.isDefault" />
                  </label>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !mbomValid">
                  <FieldProLabel for="pv-mbom">已发布 MBOM <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.mbomVersionId">
                    <SelectProTrigger id="pv-mbom"><SelectProValue placeholder="选择已发布 MBOM" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in mbomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>仅可选择已发布的 MBOM。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !routingValid">
                  <FieldProLabel for="pv-routing">已发布工艺路线 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.routingVersionId">
                    <SelectProTrigger id="pv-routing"><SelectProValue placeholder="选择已发布工艺路线" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in routingOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>仅可选择已发布的工艺路线。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>

              <FormSectionTitle>有效期与适用范围</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && (!validFromValid || !validRangeValid)">
                  <FieldProLabel>生效起 <span class="text-destructive">*</span></FieldProLabel>
                  <DatePickerPro v-model="form.validFrom" placeholder="选择生效起日" class="w-full" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !validRangeValid">
                  <FieldProLabel>生效止</FieldProLabel>
                  <DatePickerPro v-model="form.validTo" placeholder="留空表示长期有效" class="w-full" />
                  <FieldProDescription>留空表示长期有效；填写时须不早于生效起日。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !lotRangeValid">
                  <FieldProLabel for="pv-lot-min">批量下限</FieldProLabel>
                  <InputPro id="pv-lot-min" v-model="form.lotSizeMin" type="number" min="0" placeholder="不限" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !lotRangeValid">
                  <FieldProLabel for="pv-lot-max">批量上限</FieldProLabel>
                  <InputPro id="pv-lot-max" v-model="form.lotSizeMax" type="number" min="0" placeholder="不限" />
                  <FieldProDescription>下限须不大于上限；两者皆可留空表示不限。</FieldProDescription>
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="pv-priority">优先级</FieldProLabel>
                  <InputPro id="pv-priority" v-model="form.priority" type="number" min="0" />
                  <FieldProDescription>命中多个版本时数值越大越优先。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingId ? '保存修改' : '创建版本' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="有效生产版本" :value="activeCount" hint="在有效期内、可投产的版本" />
      <SectionCard description="默认版本" :value="defaultCount" hint="一料多版时默认采用的版本" />
    </SectionCards>

    <CardPro>
      <CardProHeader>
        <CardProTitle class="text-base">版本解析</CardProTitle>
        <p class="text-sm text-muted-foreground">选物料、生效日和批量，查此时投产该用哪个版本。</p>
      </CardProHeader>
      <CardProContent class="grid gap-3 md:grid-cols-2 lg:grid-cols-[repeat(3,minmax(0,1fr))_auto]">
        <FieldPro>
          <FieldProLabel for="resolve-sku">物料</FieldProLabel>
          <SelectPro v-model="resolveForm.skuCode">
            <SelectProTrigger id="resolve-sku"><SelectProValue placeholder="选择物料" /></SelectProTrigger>
            <SelectProContent>
              <SelectProItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
            </SelectProContent>
          </SelectPro>
        </FieldPro>
        <FieldPro>
          <FieldProLabel>生效日</FieldProLabel>
          <DatePickerPro v-model="resolveForm.effectiveDate" placeholder="选择生效日" class="w-full" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="resolve-lot">批量</FieldProLabel>
          <InputPro id="resolve-lot" v-model="resolveForm.lotSize" type="number" min="0" />
        </FieldPro>
        <div class="flex items-end gap-2">
          <ButtonPro type="button" :disabled="!canResolve || resolvePending" @click="runResolve">
            <Spinner v-if="resolvePending" aria-hidden="true" />
            <SearchIcon v-else aria-hidden="true" />
            解析
          </ButtonPro>
          <ButtonPro v-if="resolvedOnce" type="button" variant="ghost" @click="clearResolve">清除</ButtonPro>
        </div>
        <div v-if="resolvedOnce" class="grid gap-2 rounded-md border bg-muted/30 p-3 text-sm lg:col-span-full">
          <template v-if="resolved">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">命中物料</span>
              <span class="font-medium">{{ skuLabel(resolved.skuCode) }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">MBOM</span>
              <span class="font-medium">{{ resolved.mbomVersionId ?? '无' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">工艺路线</span>
              <span class="font-medium">{{ resolved.routingVersionId ?? '无' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <StatusBadgePro :label="statusLabel(resolved.status)" :tone="statusTone(resolved.status)" />
            </div>
          </template>
          <p v-else class="text-muted-foreground">
            该物料在所选生效日与批量下没有命中任何生产版本。可调整条件，或新建覆盖此区间的版本。
          </p>
        </div>
      </CardProContent>
    </CardPro>

    <Toolbar v-model:search="skuSearch" search-placeholder="按物料编码筛选生产版本">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="状态筛选"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="o in STATUS_FILTER_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="productionVersionsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="productionVersions"
      row-key="productionVersionId"
      :loading="productionVersionsPending"
      empty-message="当前范围没有生产版本。可新建版本把物料绑定到已发布的 MBOM 与工艺路线。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuLabel(row.skuCode) }}</span>
          <span class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-binding="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ row.mbomVersionId || '无' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.routingVersionId || '无' }}</span>
        </div>
      </template>
      <template #cell-valid="{ row }">
        <span class="text-muted-foreground">{{ formatValidRange(row.validFrom, row.validTo) }}</span>
      </template>
      <template #cell-lotSize="{ row }">{{ formatLotRange(row.lotSizeMin, row.lotSizeMax) }}</template>
      <template #cell-priority="{ row }"><span class="tabular-nums">{{ row.priority ?? 0 }}</span></template>
      <template #cell-isDefault="{ row }">
        <StatusBadgePro v-if="row.isDefault" label="默认" tone="info" />
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro :label="statusLabel(row.status)" :tone="statusTone(row.status)" />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <ButtonPro
            type="button"
            variant="ghost"
            size="sm"
            :disabled="row.status?.toLowerCase() === 'archived'"
            @click="openEdit(row)"
          >
            编辑
          </ButtonPro>
          <ButtonPro
            type="button"
            variant="ghost"
            size="sm"
            :disabled="row.status?.toLowerCase() === 'archived'"
            @click="openArchive(row)"
          >
            归档
          </ButtonPro>
        </div>
      </template>
    </DataTablePro>


    <AlertDialogPro v-model:open="archiveOpen">
      <AlertDialogProContent>
        <AlertDialogProHeader>
          <AlertDialogProTitle>归档生产版本</AlertDialogProTitle>
          <AlertDialogProDescription>
            归档后该版本不可再编辑，也不会被新工单引用。物料「{{ skuLabel(archiveTarget?.skuCode) }}」的此版本将被归档。
          </AlertDialogProDescription>
        </AlertDialogProHeader>
        <FieldPro class="px-1">
          <FieldProLabel for="archive-reason">归档原因</FieldProLabel>
          <InputPro id="archive-reason" v-model="archiveReason" placeholder="例如：工艺变更，已切换到新版本" />
          <FieldProDescription>留空将记录默认原因「不再用于排产」。</FieldProDescription>
        </FieldPro>
        <AlertDialogProFooter>
          <AlertDialogProCancel>取消</AlertDialogProCancel>
          <AlertDialogProAction :disabled="archivePending" @click="confirmArchive">
            <Spinner v-if="archivePending" aria-hidden="true" />
            确认归档
          </AlertDialogProAction>
        </AlertDialogProFooter>
      </AlertDialogProContent>
    </AlertDialogPro>
  </BusinessLayout>
</template>
