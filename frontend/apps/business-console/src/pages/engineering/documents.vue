<script setup lang="ts">
import type {
  BusinessConsoleEngineeringDocumentItem,
  BusinessConsoleRegisterEngineeringDocumentRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useEngineeringDocuments, useEngineeringItems } from '@/composables/useProductEngineering'
import {
  ENGINEERING_DOCUMENT_TYPE_ALIASES,
  ENGINEERING_DOCUMENT_TYPE_OPTIONS,
} from '@/data/engineeringReference'
import { refLabel } from '@/data/masterDataReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvEntityPicker,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '工程文档',
    requiredPermissions: ['business.engineering.documents.read'],
  },
})

const {
  documents,
  documentsError,
  documentsPending,
  documentsTotal,
  filters,
  refresh,
  registerDocument,
  registerPending,
  fetchDocumentDetail,
} = useEngineeringDocuments()

// 文档类型是固定受控值（后端无对应 CodeSet），筛选与表单共用同一份常量，杜绝手输拼写漂移。
const documentTypeOptions = ENGINEERING_DOCUMENT_TYPE_OPTIONS
function documentTypeLabel(value?: string | null) {
  const raw = (value ?? '').trim()
  if (!raw) return '—'
  // 受控值先查；存量数据里的既有类型码（sop / inspection-spec / process-card）走只读别名表。
  const alias = ENGINEERING_DOCUMENT_TYPE_ALIASES[raw.toLowerCase()]
  if (alias) return alias
  const label = refLabel(documentTypeOptions, raw)
  if (label === raw && import.meta.env.DEV) {
    console.warn(`[工程文档] 词表缺失: ${raw}，请补 engineeringReference.ts 的文档类型词表`)
  }
  return label
}

const documentTypeFilter = computed({
  get: () => filters.documentType ?? 'all',
  set: (value: string) => {
    filters.documentType = value === 'all' ? undefined : value
  },
})

// 关联物料从工程物料目录里选（同一 itemCode 多修订只出现一次）。
const { items: engineeringItems, itemsPending: engineeringItemsPending } = useEngineeringItems()

const itemSearch = computed({
  get: () => filters.itemCode ?? '',
  set: (value: string) => {
    filters.itemCode = value.trim() ? value : undefined
  },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

const docTypeCount = computed(
  () => new Set(documents.value.map((d) => d.documentType).filter(Boolean)).size,
)
const linkedCount = computed(() => documents.value.filter((d) => d.itemCode).length)
const documentCells = computed<NvMetricStripCell[]>(() => [
  { key: 'types', label: '文档类型', value: docTypeCount.value, unit: '类' },
  {
    key: 'linked',
    label: '已关联物料',
    value: linkedCount.value,
    unit: '个',
    meta: '未关联物料的文档无法在物料页看到',
  },
])

const listErrorMessage = computed(() => formatError(documentsError.value))

const columns: NvDataTableColumn<BusinessConsoleEngineeringDocumentItem>[] = [
  { key: 'documentNumber', header: '文档号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'documentType', header: '类型', width: 'w-28' },
  { key: 'fileName', header: '文件名' },
  { key: 'itemCode', header: '关联物料', width: 'w-32' },
  { key: 'registeredAtUtc', header: '登记时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 登记文档向导 ──────────────────────────────────────────────
// 后端无文件上传通道，fileId 先作文件引用 ID 文本输入（标注上传待接入，不假装能上传）。
interface DocumentForm {
  documentNumber: string
  revision: string
  documentType: string
  fileId: string
  fileName: string
  contentType: string
  itemCode: string
}
function blankForm(): DocumentForm {
  return {
    documentNumber: '',
    revision: '',
    documentType: '',
    fileId: '',
    fileName: '',
    contentType: 'application/pdf',
    itemCode: '',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<DocumentForm>(blankForm())

const itemPickerOptions = computed(() => {
  const byCode = new Map<string, { value: string; label: string; hint?: string }>()
  for (const item of engineeringItems.value) {
    if (!item.itemCode || byCode.has(item.itemCode)) continue
    byCode.set(item.itemCode, {
      value: item.itemCode,
      label: item.name ?? item.itemCode,
      hint: item.itemCode,
    })
  }
  const options = [...byCode.values()]
  // 深链 / 目录截断时保住已填编码，避免选择器显示成未选。
  const current = form.itemCode.trim()
  if (current && !options.some((option) => option.value === current)) {
    options.unshift({ value: current, label: current })
  }
  return options
})

// 文档号可留空由后端取号（coding allocator，形如 EDOC-20260731-000001）——手填只是为了沿用既有编号。
// 台账 #34：此前文档号必填，手填撞号只能靠提交后的 400 才知道，且那条 400 还是英文、被兜底吞掉。
const revisionValid = computed(() => form.revision.trim().length > 0)
const documentTypeValid = computed(() => form.documentType.trim().length > 0)
const fileIdValid = computed(() => form.fileId.trim().length > 0)
const fileNameValid = computed(() => form.fileName.trim().length > 0)
const contentTypeValid = computed(() => form.contentType.trim().length > 0)
const canSubmit = computed(
  () =>
    revisionValid.value &&
    documentTypeValid.value &&
    fileIdValid.value &&
    fileNameValid.value &&
    contentTypeValid.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}

/**
 * 当前页里已有同号同修订？——提交前的**占用预检**，只看已加载的行，
 * 因此措辞是「已在列表中」而不是「已存在」：真正的权威判定在后端（撞号给中文 400）。
 */
const documentNumberTaken = computed(() => {
  const number = form.documentNumber.trim().toLowerCase()
  const revision = form.revision.trim().toLowerCase()
  if (!number || !revision) return false
  return documents.value.some(
    (row) =>
      (row.documentNumber ?? '').trim().toLowerCase() === number &&
      (row.revision ?? '').trim().toLowerCase() === revision,
  )
})

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const documentNumber = form.documentNumber.trim()
  const body: BusinessConsoleRegisterEngineeringDocumentRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    // 留空 = 交给后端 coding allocator 取号（契约上 documentNumber 本就可选）。
    documentNumber: documentNumber || undefined,
    revision: form.revision.trim(),
    fileId: form.fileId.trim(),
    fileName: form.fileName.trim(),
    contentType: form.contentType.trim(),
    documentType: form.documentType.trim(),
    itemCode: form.itemCode.trim() || undefined,
  }
  try {
    const result = (await registerDocument(body)) as { data?: { id?: string | null } } | undefined
    // 自动取号时号码只有后端知道：从回执里取出来告诉用户，别让他去列表里猜哪条是新的。
    const registeredNumber = documentNumber || (result?.data?.id ?? '').trim()
    notifySuccess(
      registeredNumber
        ? `已登记文档「${registeredNumber}」修订 ${form.revision.trim()}。`
        : `已登记文档修订 ${form.revision.trim()}。`,
    )
    showErrors.value = false
    formOpen.value = false
  } catch (error) {
    notifyOperationFailure('登记文档修订失败', error, '登记文档修订失败，请稍后重试。')
  }
}

// ── 查看文档明细（get-by-id）────────────────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringDocumentItem | null>(null)
const detailPending = ref(false)
async function openView(row: BusinessConsoleEngineeringDocumentItem) {
  viewTarget.value = row
  viewOpen.value = true
  if (!row.documentNumber || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchDocumentDetail(row.documentNumber, row.revision)
    if (detail) viewTarget.value = detail
  } catch (error) {
    // 结果一律 toast；列表行数据仍可展示，不在抽屉里留常驻错误条。
    notifyError(error, '加载文档明细失败，请稍后重试。')
  } finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工程文档"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${documentsTotal} 个文档`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="documentsPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="formOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              登记文档
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-xl">
            <NvDialogHeader>
              <NvDialogTitle>登记工程文档</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only"
                >按文档号与修订登记工程文档。</NvDialogDescription
              >
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项。
              </p>

              <FormSectionTitle>文档标识</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-3">
                <NvField>
                  <NvFieldLabel for="doc-number">文档号</NvFieldLabel>
                  <NvInput
                    id="doc-number"
                    v-model="form.documentNumber"
                    placeholder="留空自动取号"
                  />
                  <NvFieldDescription v-if="documentNumberTaken" class="text-warning">
                    该文档号的这个修订已在列表中，换修订号或留空自动取号。
                  </NvFieldDescription>
                  <NvFieldDescription v-else
                    >留空由系统取号；填了就沿用既有编号。</NvFieldDescription
                  >
                </NvField>
                <NvField :data-invalid="showErrors && !revisionValid">
                  <NvFieldLabel for="doc-rev"
                    >修订号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="doc-rev" v-model="form.revision" placeholder="如 A、B" />
                </NvField>
                <NvField :data-invalid="showErrors && !documentTypeValid">
                  <NvFieldLabel for="doc-type"
                    >文档类型 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="form.documentType">
                    <NvSelectTrigger id="doc-type">
                      <NvSelectValue placeholder="选择文档类型" />
                    </NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in documentTypeOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </NvFieldGroup>

              <FormSectionTitle>文件引用</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField class="sm:col-span-2" :data-invalid="showErrors && !fileIdValid">
                  <NvFieldLabel for="doc-file-id"
                    >文件引用 ID <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="doc-file-id"
                    v-model="form.fileId"
                    placeholder="填写文件存储引用 ID"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !fileNameValid">
                  <NvFieldLabel for="doc-file-name"
                    >文件名 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="doc-file-name"
                    v-model="form.fileName"
                    placeholder="如 drawing.pdf"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !contentTypeValid">
                  <NvFieldLabel for="doc-content-type"
                    >内容类型 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="doc-content-type"
                    v-model="form.contentType"
                    placeholder="如 application/pdf"
                  />
                </NvField>
              </NvFieldGroup>

              <FormSectionTitle>关联（可选）</FormSectionTitle>
              <NvField>
                <NvFieldLabel for="doc-item-code">关联物料</NvFieldLabel>
                <NvEntityPicker
                  id="doc-item-code"
                  v-model="form.itemCode"
                  :options="itemPickerOptions"
                  title="选择关联物料"
                  placeholder="可留空"
                  source-text="数据来自工程物料目录"
                  empty-text="暂无工程物料，请先在工程物料维护物料修订"
                  :loading="engineeringItemsPending"
                  aria-label="关联物料"
                  clearable
                />
              </NvField>

              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
                <NvButton type="submit" :disabled="registerPending">
                  <Spinner v-if="registerPending" aria-hidden="true" />
                  登记文档
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="documentCells" />

    <NvToolbar v-model:search="itemSearch" search-placeholder="按关联物料编码筛选">
      <template #filters>
        <NvSelect v-model="documentTypeFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="文档类型筛选">
            <NvSelectValue placeholder="全部类型" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部类型</NvSelectItem>
            <NvSelectItem
              v-for="option in documentTypeOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="documentsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="documents"
      :row-key="(r) => `${r.documentNumber}:${r.revision}`"
      :loading="documentsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有工程文档。可登记文档号 + 修订，并填写文件引用 ID 与类型。"
    >
      <template #cell-documentType="{ row }">{{ documentTypeLabel(row.documentType) }}</template>
      <template #cell-itemCode="{ row }">{{ row.itemCode || '—' }}</template>
      <template #cell-registeredAtUtc="{ row }">{{ formatDateTime(row.registeredAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openView(row)">查看</NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="viewOpen">
      <NvSheetContent class="sm:max-w-md">
        <NvSheetHeader>
          <NvSheetTitle>工程文档 · 明细</NvSheetTitle>
          <NvSheetDescription>
            {{ viewTarget ? `${viewTarget.documentNumber} · 修订 ${viewTarget.revision}` : '' }}
          </NvSheetDescription>
        </NvSheetHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2">
          <div
            v-if="detailPending"
            class="flex items-center gap-2 py-4 text-sm text-muted-foreground"
          >
            <Spinner aria-hidden="true" />
            加载文档明细…
          </div>
          <div v-else class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">类型</span>
              <span class="font-medium">{{ documentTypeLabel(viewTarget.documentType) }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">文件名</span>
              <span class="font-medium">{{ viewTarget.fileName || '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">内容类型</span>
              <span class="font-medium">{{ viewTarget.contentType || '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">文件引用 ID</span>
              <span class="font-medium break-all text-right">{{ viewTarget.fileId || '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">关联物料</span>
              <span class="font-medium">{{ viewTarget.itemCode || '无' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">登记时间</span>
              <span class="font-medium">{{ formatDateTime(viewTarget.registeredAtUtc) }}</span>
            </div>
          </div>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
