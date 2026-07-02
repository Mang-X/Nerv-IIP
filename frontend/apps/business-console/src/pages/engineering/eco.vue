<script setup lang="ts">
import type {
  BusinessConsoleEngineeringChangeItem,
  BusinessConsoleReleaseEngineeringChangeRequest,
} from '@nerv-iip/api-client'
import type { DataTableProColumn, StatusTone } from '@nerv-iip/ui'
import BusinessDocumentApprovalPanel from '@/components/business/BusinessDocumentApprovalPanel.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useEngineeringChanges } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
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
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '工程变更', requiredPermissions: ['business.engineering.changes.read'] } })

const {
  changes,
  changesError,
  changesPending,
  changesTotal,
  filters,
  refresh,
  releaseChange,
  releasePending,
  fetchChangeDetail,
} = useEngineeringChanges()

// 受影响版本的对象种类（versionKind）。后端按字符串接收，这里给贴切的工程对象枚举。
const VERSION_KIND_OPTIONS = [
  { label: '设计 BOM（EBOM）', value: 'EngineeringBom' },
  { label: '制造 BOM（MBOM）', value: 'ManufacturingBom' },
  { label: '工艺路线', value: 'Routing' },
  { label: '生产版本', value: 'ProductionVersion' },
]
function versionKindLabel(kind?: string | null) {
  return VERSION_KIND_OPTIONS.find((o) => o.value === kind)?.label ?? (kind || '—')
}

// 后端是一步发布（Open→Approve→Release），变更落库即为已发布状态——不假造草稿/待审。
const STATUS_FILTER_OPTIONS = [
  { label: '全部状态', value: 'all' },
  { label: '已发布', value: 'Released' },
]
const statusFilter = ref('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function ecoStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'released' || s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '已发布', tone: 'success' }
}

const releasedCount = computed(() => changes.value.length)
const affectedTotal = computed(() =>
  changes.value.reduce((sum, c) => sum + (c.affectedVersions?.length ?? 0), 0),
)

const listErrorMessage = computed(() => formatError(changesError.value))

const columns: DataTableProColumn<BusinessConsoleEngineeringChangeItem>[] = [
  { key: 'changeNumber', header: '变更号', cellClass: 'font-medium' },
  { key: 'reason', header: '变更原因' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'affected', header: '受影响版本', width: 'w-28', align: 'end' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 发布变更向导（一步发布，非多步审批）────────────────────────
interface AffectedRow {
  versionKind: string
  versionId: string
}
interface EcoForm {
  reason: string
  approvalReferenceId: string
  effectiveDate: string | null
  affectedVersions: AffectedRow[]
}
function blankAffected(): AffectedRow {
  return { versionKind: '', versionId: '' }
}
function blankForm(): EcoForm {
  return { reason: '', approvalReferenceId: '', effectiveDate: today(), affectedVersions: [blankAffected()] }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<EcoForm>(blankForm())

const reasonValid = computed(() => form.reason.trim().length > 0)
const approvalValid = computed(() => form.approvalReferenceId.trim().length > 0)
const effectiveValid = computed(() => !!form.effectiveDate)
function affectedValid(row: AffectedRow) {
  return row.versionKind.trim().length > 0 && row.versionId.trim().length > 0
}
const affectedListValid = computed(() =>
  form.affectedVersions.length > 0 && form.affectedVersions.every(affectedValid),
)
const canSubmit = computed(() =>
  reasonValid.value && approvalValid.value && effectiveValid.value && affectedListValid.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function addAffected() {
  form.affectedVersions.push(blankAffected())
}
function removeAffected(index: number) {
  if (form.affectedVersions.length <= 1) return
  form.affectedVersions.splice(index, 1)
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleReleaseEngineeringChangeRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    reason: form.reason.trim(),
    approvalReferenceId: form.approvalReferenceId.trim(),
    effectiveDate: form.effectiveDate ?? undefined,
    affectedVersions: form.affectedVersions.map((row) => ({
      versionKind: row.versionKind.trim(),
      versionId: row.versionId.trim(),
    })),
  }
  try {
    await releaseChange(body)
    notifySuccess(`已发布工程变更，受影响版本 ${form.affectedVersions.length} 个。`)
    showErrors.value = false
    formOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 查看变更明细（get-by-id 看受影响版本）──────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringChangeItem | null>(null)
const detailPending = ref(false)
const detailError = ref('')
const viewAffected = computed(() => viewTarget.value?.affectedVersions ?? [])
async function openView(row: BusinessConsoleEngineeringChangeItem) {
  viewTarget.value = row
  viewOpen.value = true
  detailError.value = ''
  if (!row.changeNumber) return
  detailPending.value = true
  try {
    const detail = await fetchChangeDetail(row.changeNumber)
    if (detail) viewTarget.value = detail
  }
  catch (error) {
    detailError.value = formatError(error) || '加载受影响版本失败，请稍后重试。'
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
      title="工程变更"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${changesTotal} 个变更`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="changesPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              发布变更
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>发布工程变更</DialogProTitle>
              <DialogProDescription>
                变更一步发布并即时生效。先关联真实审批链，再填写变更原因、生效日与受影响版本。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项，并确保至少一条受影响版本填好对象种类与版本 ID。
              </p>

              <FormSectionTitle>变更信息</FormSectionTitle>
              <FieldProGroup class="grid gap-3">
                <FieldPro :data-invalid="showErrors && !reasonValid">
                  <FieldProLabel for="eco-reason">变更原因 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="eco-reason" v-model="form.reason" placeholder="说明本次变更的内容与原因" />
                </FieldPro>
                <BusinessDocumentApprovalPanel
                  v-model="form.approvalReferenceId"
                  title="变更审批链"
                  source-service="product-engineering"
                  document-type="engineering-change-order"
                  :allow-start="false"
                />
                <p v-if="showErrors && !approvalValid" class="text-sm text-destructive" role="alert">
                  请先关联一条真实审批链，再发布工程变更。
                </p>
                <div class="grid gap-3 sm:grid-cols-2">
                  <FieldPro :data-invalid="showErrors && !effectiveValid">
                    <FieldProLabel>生效日 <span class="text-destructive">*</span></FieldProLabel>
                    <DatePickerPro v-model="form.effectiveDate" placeholder="选择生效日" class="w-full" />
                  </FieldPro>
                </div>
              </FieldProGroup>

              <div class="flex items-center justify-between">
                <FormSectionTitle>受影响版本</FormSectionTitle>
                <ButtonPro type="button" variant="outline" size="sm" @click="addAffected">
                  <PlusIcon aria-hidden="true" />
                  增加一条
                </ButtonPro>
              </div>
              <div class="grid gap-2">
                <div
                  v-for="(row, index) in form.affectedVersions"
                  :key="index"
                  class="grid grid-cols-[12rem_1fr_auto] items-end gap-2 rounded-md border p-2"
                >
                  <FieldPro :data-invalid="showErrors && !row.versionKind.trim()">
                    <FieldProLabel :for="`eco-kind-${index}`">对象种类 <span class="text-destructive">*</span></FieldProLabel>
                    <SelectPro v-model="row.versionKind">
                      <SelectProTrigger :id="`eco-kind-${index}`"><SelectProValue placeholder="选择对象" /></SelectProTrigger>
                      <SelectProContent>
                        <SelectProItem v-for="o in VERSION_KIND_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                      </SelectProContent>
                    </SelectPro>
                  </FieldPro>
                  <FieldPro :data-invalid="showErrors && !row.versionId.trim()">
                    <FieldProLabel :for="`eco-vid-${index}`">版本 ID <span class="text-destructive">*</span></FieldProLabel>
                    <InputPro :id="`eco-vid-${index}`" v-model="row.versionId" placeholder="受影响的版本标识" />
                  </FieldPro>
                  <ButtonPro
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="删除该受影响版本"
                    :disabled="form.affectedVersions.length <= 1"
                    @click="removeAffected(index)"
                  >
                    <Trash2Icon aria-hidden="true" />
                  </ButtonPro>
                </div>
              </div>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="releasePending">
                  <Spinner v-if="releasePending" aria-hidden="true" />
                  发布变更
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="已发布变更" :value="releasedCount" hint="当前范围内已生效的工程变更" />
      <SectionCard description="受影响版本合计" :value="affectedTotal" hint="所有变更累计影响的版本数" />
    </SectionCards>

    <Toolbar>
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
      :total-items="changesTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="changes"
      :row-key="(r) => r.changeNumber ?? ''"
      :loading="changesPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有工程变更。可发布变更，登记变更原因、审批参考与受影响版本。"
    >
      <template #cell-reason="{ row }">
        <span class="line-clamp-2 text-sm">{{ row.reason || '—' }}</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro :label="ecoStatus(row.status).label" :tone="ecoStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{ row.effectiveDate ? formatDate(row.effectiveDate) : '即时' }}</template>
      <template #cell-affected="{ row }">
        <span class="tabular-nums">{{ row.affectedVersions?.length ?? 0 }}</span>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <ButtonPro type="button" variant="ghost" size="sm" @click="openView(row)">查看</ButtonPro>
        </div>
      </template>
    </DataTablePro>


    <SheetPro v-model:open="viewOpen">
      <SheetProContent class="sm:max-w-lg">
        <SheetProHeader>
          <SheetProTitle>工程变更 · 受影响版本</SheetProTitle>
          <SheetProDescription>
            {{ viewTarget ? `${viewTarget.changeNumber} · ${viewTarget.reason ?? ''}` : '' }}
          </SheetProDescription>
        </SheetProHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2">
          <div class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <StatusBadgePro :label="ecoStatus(viewTarget.status).label" :tone="ecoStatus(viewTarget.status).tone" />
            </div>
            <BusinessDocumentApprovalPanel
              :model-value="viewTarget.approvalReferenceId ?? ''"
              title="变更审批链"
              source-service="product-engineering"
              document-type="engineering-change-order"
              :document-id="viewTarget.changeNumber ?? undefined"
              :allow-start="false"
            />
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">生效日</span>
              <span class="font-medium">{{ viewTarget.effectiveDate ? formatDate(viewTarget.effectiveDate) : '即时' }}</span>
            </div>
          </div>

          <div v-if="detailPending" class="flex items-center gap-2 py-4 text-sm text-muted-foreground">
            <Spinner aria-hidden="true" />
            加载受影响版本…
          </div>
          <p v-else-if="detailError" class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
            {{ detailError }}
          </p>
          <div v-else-if="viewAffected.length" class="overflow-hidden rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-left font-medium">对象种类</th>
                  <th class="px-3 py-2 text-left font-medium">版本 ID</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, i) in viewAffected" :key="i" class="border-t">
                  <td class="px-3 py-2">{{ versionKindLabel(row.versionKind) }}</td>
                  <td class="px-3 py-2 break-all">{{ row.versionId || '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该变更没有受影响版本记录。
          </p>
        </div>
      </SheetProContent>
    </SheetPro>
  </BusinessLayout>
</template>
