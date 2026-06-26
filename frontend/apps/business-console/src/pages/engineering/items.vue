<script setup lang="ts">
import type {
  BusinessConsoleCreateEngineeringItemRevisionRequest,
  BusinessConsoleEngineeringItemRevisionItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useEngineeringItems } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  CheckboxPro,
  DataTablePro,
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
  SheetPro,
  SheetProContent,
  SheetProDescription,
  SheetProHeader,
  SheetProTitle,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { GitBranchIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '工程物料' } })

const {
  items,
  itemsError,
  itemsPending,
  itemsTotal,
  filters,
  refresh,
  createItemRevision,
  createPending,
  fetchItemDetail,
} = useEngineeringItems()

// 状态枚举用后端真值 Published/Draft/Archived（EngineeringVersionStatus）。
const STATUS_FILTER_OPTIONS = [
  { label: '全部状态', value: 'all' },
  { label: '已发布', value: 'Published' },
  { label: '草稿', value: 'Draft' },
  { label: '已归档', value: 'Archived' },
]
const statusFilter = ref('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const itemSearch = computed({
  get: () => filters.itemCode ?? '',
  set: (value: string) => { filters.itemCode = value.trim() ? value : undefined },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function engStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(() => items.value.filter((i) => (i.status ?? '').toLowerCase() === 'published').length)
const draftCount = computed(() => items.value.filter((i) => (i.status ?? '').toLowerCase() === 'draft').length)

// 已知物料编码（用于「在已有物料上派生新修订」下拉）。去重保留首个。
const knownItemCodes = computed(() => {
  const seen = new Set<string>()
  const out: string[] = []
  for (const it of items.value) {
    const code = it.itemCode?.trim()
    if (code && !seen.has(code)) { seen.add(code); out.push(code) }
  }
  return out.sort()
})

const listErrorMessage = computed(() => formatError(itemsError.value))

const columns: DataTableProColumn<BusinessConsoleEngineeringItemRevisionItem>[] = [
  { key: 'itemCode', header: '物料编码', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'name', header: '名称' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'updatedAtUtc', header: '更新时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 新建修订向导 ──────────────────────────────────────────────
// 工程数据语义：物料不是直接改，而是派生新修订。targetMode='new' 建全新物料（编码自动），
// 'existing' 在已有物料编码上派生新修订。release 决定新修订是否立即发布。
interface ItemForm {
  targetMode: 'new' | 'existing'
  itemCode: string
  revision: string
  name: string
  release: boolean
}
function blankForm(): ItemForm {
  return { targetMode: 'new', itemCode: '', revision: '', name: '', release: false }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<ItemForm>(blankForm())

const revisionValid = computed(() => form.revision.trim().length > 0)
const nameValid = computed(() => form.name.trim().length > 0)
// 在已有物料上派生时必须选物料编码；建新物料时编码由后端自动生成，无需填。
const targetValid = computed(() => form.targetMode === 'new' || form.itemCode.trim().length > 0)
const canSubmit = computed(() => revisionValid.value && nameValid.value && targetValid.value)

function openCreate() {
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleCreateEngineeringItemRevisionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    // 建新物料时不传编码（后端自动编码）；派生时带已有物料编码。
    itemCode: form.targetMode === 'existing' ? form.itemCode.trim() : undefined,
    revision: form.revision.trim(),
    name: form.name.trim(),
    release: form.release,
  }
  try {
    await createItemRevision(body)
    notifySuccess(
      form.release
        ? `已发布物料「${form.name.trim()}」修订 ${form.revision.trim()}。`
        : `已创建物料「${form.name.trim()}」修订 ${form.revision.trim()}（草稿）。`,
    )
    showErrors.value = false
    formOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 查看修订明细（get-by-id）────────────────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringItemRevisionItem | null>(null)
const detailPending = ref(false)
const detailError = ref('')
async function openView(row: BusinessConsoleEngineeringItemRevisionItem) {
  viewTarget.value = row
  viewOpen.value = true
  detailError.value = ''
  if (!row.itemCode || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchItemDetail(row.itemCode, row.revision)
    if (detail) viewTarget.value = detail
  }
  catch (error) {
    detailError.value = formatError(error) || '加载物料修订明细失败，请稍后重试。'
  }
  finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="工程物料"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${itemsTotal} 个修订`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="itemsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建修订
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-xl">
            <DialogProHeader>
              <DialogProTitle>新建物料修订</DialogProTitle>
              <DialogProDescription>
                工程物料不直接编辑，而是派生新修订。可新建一个物料，或在已有物料上派生下一修订。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项；在已有物料上派生时还需选择物料编码。
              </p>

              <FormSectionTitle>派生对象</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro>
                  <FieldProLabel for="item-mode">派生方式</FieldProLabel>
                  <SelectPro v-model="form.targetMode">
                    <SelectProTrigger id="item-mode"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem value="new">新建物料</SelectProItem>
                      <SelectProItem value="existing">在已有物料上派生</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>
                    新建物料时编码由系统自动生成；派生时沿用所选物料编码。
                  </FieldProDescription>
                </FieldPro>
                <FieldPro v-if="form.targetMode === 'existing'" :data-invalid="showErrors && !targetValid">
                  <FieldProLabel for="item-code">物料编码 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.itemCode">
                    <SelectProTrigger id="item-code"><SelectProValue placeholder="选择已有物料" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="code in knownItemCodes" :key="code" :value="code">{{ code }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>从当前列表里已存在的物料编码中选择。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>

              <FormSectionTitle>修订内容</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !revisionValid">
                  <FieldProLabel for="item-rev">修订号 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="item-rev" v-model="form.revision" placeholder="如 A、B、001" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !nameValid">
                  <FieldProLabel for="item-name">名称 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="item-name" v-model="form.name" placeholder="物料名称" />
                </FieldPro>
              </FieldProGroup>

              <FieldPro>
                <FieldProLabel>发布</FieldProLabel>
                <label
                  for="item-release"
                  class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm"
                >
                  <span>创建后立即发布该修订</span>
                  <CheckboxPro id="item-release" v-model:checked="form.release" />
                </label>
                <FieldProDescription>不勾选则保存为草稿；发布后该修订不可变。</FieldProDescription>
              </FieldPro>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPending">
                  <Spinner v-if="createPending" aria-hidden="true" />
                  {{ form.release ? '创建并发布' : '创建草稿' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="已发布修订" :value="publishedCount" hint="可供设计 BOM 引用的物料修订" />
      <SectionCard description="草稿修订" :value="draftCount" hint="尚未发布、可继续完善的修订" />
    </SectionCards>

    <Toolbar v-model:search="itemSearch" search-placeholder="按物料编码筛选">
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
      :total-items="itemsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="items"
      :row-key="(r) => `${r.itemCode}:${r.revision}`"
      :loading="itemsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有工程物料。可新建一个物料，或在已有物料上派生新修订。"
    >
      <template #cell-status="{ row }">
        <StatusBadgePro :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-updatedAtUtc="{ row }">{{ formatDateTime(row.updatedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <ButtonPro type="button" variant="ghost" size="sm" @click="openView(row)">查看</ButtonPro>
        </div>
      </template>
    </DataTablePro>


    <SheetPro v-model:open="viewOpen">
      <SheetProContent class="sm:max-w-md">
        <SheetProHeader>
          <SheetProTitle>工程物料 · 修订明细</SheetProTitle>
          <SheetProDescription>
            {{ viewTarget ? `${viewTarget.itemCode} · 修订 ${viewTarget.revision}` : '' }}
          </SheetProDescription>
        </SheetProHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2">
          <div v-if="detailPending" class="flex items-center gap-2 py-4 text-sm text-muted-foreground">
            <Spinner aria-hidden="true" />
            加载修订明细…
          </div>
          <p v-else-if="detailError" class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
            {{ detailError }}
          </p>
          <div v-else class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">名称</span>
              <span class="font-medium">{{ viewTarget.name || '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <StatusBadgePro :label="engStatus(viewTarget.status).label" :tone="engStatus(viewTarget.status).tone" />
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">创建时间</span>
              <span class="font-medium">{{ formatDateTime(viewTarget.createdAtUtc) }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">更新时间</span>
              <span class="font-medium">{{ formatDateTime(viewTarget.updatedAtUtc) }}</span>
            </div>
            <p class="flex items-center gap-1.5 pt-1 text-xs text-muted-foreground">
              <GitBranchIcon class="size-3.5" aria-hidden="true" />
              修改请在此物料上派生新修订，已发布修订不可变。
            </p>
          </div>
        </div>
      </SheetProContent>
    </SheetPro>
  </BusinessLayout>
</template>
