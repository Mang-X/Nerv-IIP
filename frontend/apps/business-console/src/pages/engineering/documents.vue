<script setup lang="ts">
import type {
  BusinessConsoleEngineeringDocumentItem,
  BusinessConsoleRegisterEngineeringDocumentRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useEngineeringDocuments } from '@/composables/useProductEngineering'
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
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvToolbar,
} from '@nerv-iip/ui'
import { FileTextIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

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

const documentTypeSearch = computed({
  get: () => filters.documentType ?? '',
  set: (value: string) => {
    filters.documentType = value.trim() ? value : undefined
  },
})

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

const documentNumberValid = computed(() => form.documentNumber.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0)
const documentTypeValid = computed(() => form.documentType.trim().length > 0)
const fileIdValid = computed(() => form.fileId.trim().length > 0)
const fileNameValid = computed(() => form.fileName.trim().length > 0)
const contentTypeValid = computed(() => form.contentType.trim().length > 0)
const canSubmit = computed(
  () =>
    documentNumberValid.value &&
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

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleRegisterEngineeringDocumentRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    documentNumber: form.documentNumber.trim(),
    revision: form.revision.trim(),
    fileId: form.fileId.trim(),
    fileName: form.fileName.trim(),
    contentType: form.contentType.trim(),
    documentType: form.documentType.trim(),
    itemCode: form.itemCode.trim() || undefined,
  }
  try {
    await registerDocument(body)
    notifySuccess(`已登记文档「${form.documentNumber.trim()}」修订 ${form.revision.trim()}。`)
    showErrors.value = false
    formOpen.value = false
  } catch (error) {
    notifyError(error)
  }
}

// ── 查看文档明细（get-by-id）────────────────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringDocumentItem | null>(null)
const detailPending = ref(false)
const detailError = ref('')
async function openView(row: BusinessConsoleEngineeringDocumentItem) {
  viewTarget.value = row
  viewOpen.value = true
  detailError.value = ''
  if (!row.documentNumber || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchDocumentDetail(row.documentNumber, row.revision)
    if (detail) viewTarget.value = detail
  } catch (error) {
    detailError.value = formatError(error) || '加载文档明细失败，请稍后重试。'
  } finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
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
              <NvDialogDescription>
                按文档号 + 修订登记一份工程文档及其文件引用。带 * 为必填项。
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项。
              </p>

              <FormSectionTitle>文档标识</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-3">
                <NvField :data-invalid="showErrors && !documentNumberValid">
                  <NvFieldLabel for="doc-number"
                    >文档号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="doc-number"
                    v-model="form.documentNumber"
                    placeholder="如 DOC-0001"
                  />
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
                  <NvInput
                    id="doc-type"
                    v-model="form.documentType"
                    placeholder="如 图纸、规格书"
                  />
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
                  <NvFieldDescription>
                    文件上传待接入，先填已存在的文件引用 ID（不在此页直接上传文件）。
                  </NvFieldDescription>
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
                <NvFieldLabel for="doc-item-code">关联物料编码</NvFieldLabel>
                <NvInput id="doc-item-code" v-model="form.itemCode" placeholder="可留空" />
                <NvFieldDescription>如该文档对应某工程物料，填其编码以便追溯。</NvFieldDescription>
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

    <NvSectionCards :columns="2">
      <NvSectionCard
        description="文档类型数"
        :value="docTypeCount"
        hint="当前范围内不同的文档类型"
      />
      <NvSectionCard description="已关联物料" :value="linkedCount" hint="挂接到工程物料的文档" />
    </NvSectionCards>

    <NvToolbar v-model:search="itemSearch" search-placeholder="按关联物料编码筛选">
      <template #filters>
        <NvInput
          v-model="documentTypeSearch"
          class="h-9 w-40"
          placeholder="按文档类型筛选"
          aria-label="文档类型筛选"
        />
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
          <p
            v-else-if="detailError"
            class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
            role="alert"
          >
            {{ detailError }}
          </p>
          <div v-else class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">类型</span>
              <span class="font-medium">{{ viewTarget.documentType || '—' }}</span>
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
            <p class="flex items-center gap-1.5 pt-1 text-xs text-muted-foreground">
              <FileTextIcon class="size-3.5" aria-hidden="true" />
              文件下载待接入，当前仅登记文件引用。
            </p>
          </div>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
