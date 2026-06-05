<script setup lang="ts">
import type {
  BusinessConsoleNcrCloseRequest,
  BusinessConsoleNcrDispositionRequest,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
  Button,
  DataTable,
  DataTablePagination,
  DropdownMenuItem,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '不合格品处理' } })

const route = useRoute()
const initialNcrKeyword = firstQuery(route.query.ncrId)
const {
  closeNcr,
  closeNcrError,
  closeNcrPending,
  filters,
  ncrs,
  ncrsError,
  ncrsPending,
  ncrsTotal,
  refreshNcrs,
  submitDisposition,
  submitDispositionError,
  submitDispositionPending,
} = useQualityNcrs(initialNcrKeyword ? { keyword: initialNcrKeyword } : {})
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status, () => filters.keyword] })

const selectedNcr = shallowRef<BusinessConsoleQualityItem>()
const detailOpen = shallowRef(false)
const dispositionSuccess = shallowRef('')
const closeSuccess = shallowRef('')
const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '待处理', value: 'open' },
  { label: '处置中', value: 'dispositioned' },
  { label: '已关闭', value: 'closed' },
]

const dispositionForm = reactive({
  dispositionType: 'use-as-is',
  dispositionApprovalChainId: '',
  attachmentFileIds: '',
})
const closeForm = reactive({
  reworkWorkOrderId: '',
  scrapMovementId: '',
  returnDocumentId: '',
})

// 上下文穿透：从工单带入时，关闭动作的返工工单默认填入来源工单。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
// 从 MES 质量项点具体 NCR 带入时，定位并自动打开对应 NCR 处置抽屉。
const targetNcrId = computed(() => firstQuery(route.query.ncrId))
const targetNcr = computed(() =>
  targetNcrId.value ? ncrs.value.find((n) => n.id === targetNcrId.value || n.code === targetNcrId.value) : undefined,
)
const targetNcrMissing = computed(() => !!targetNcrId.value && !ncrsPending.value && !targetNcr.value)
const locatedTargetId = shallowRef('')

const listErrorMessage = computed(() => formatError(ncrsError.value))
const dispositionErrorMessage = computed(() => formatError(submitDispositionError.value))
const closeErrorMessage = computed(() => formatError(closeNcrError.value))
const selectedNcrId = computed(() => selectedNcr.value?.id ?? '')
const canSubmitDisposition = computed(() => isNonEmpty(selectedNcrId.value) && isNonEmpty(dispositionForm.dispositionType))
const canCloseNcr = computed(() => isNonEmpty(selectedNcrId.value))
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const openCount = computed(() => ncrs.value.filter((r) => (r.status ?? '').toLowerCase() !== 'closed').length)
const closedCount = computed(() => ncrs.value.filter((r) => (r.status ?? '').toLowerCase() === 'closed').length)

type NcrRow = BusinessConsoleQualityItem
const columns: DataTableColumn<NcrRow>[] = [
  { key: 'code', header: 'NCR', cellClass: 'font-medium', accessor: (r) => r.code ?? r.id ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'summary', header: '摘要', accessor: (r) => qualityItemSummary(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function openNcr(ncr: BusinessConsoleQualityItem) {
  selectedNcr.value = ncr
  dispositionSuccess.value = ''
  closeSuccess.value = ''
  closeForm.reworkWorkOrderId = contextWorkOrderId.value || (isPresent(ncr.sourceDocumentId) ? ncr.sourceDocumentId : '')
  detailOpen.value = true
}

async function submitNcrDisposition() {
  if (!canSubmitDisposition.value) return
  const body: BusinessConsoleNcrDispositionRequest = {
    dispositionType: dispositionForm.dispositionType.trim(),
    dispositionApprovalChainId: optionalText(dispositionForm.dispositionApprovalChainId),
    attachmentFileIds: splitCsv(dispositionForm.attachmentFileIds),
  }
  await submitDisposition(selectedNcrId.value, body)
  dispositionSuccess.value = `不合格品 ${selectedNcr.value?.code ?? selectedNcrId.value} 处置已提交。`
}

async function submitCloseNcr() {
  if (!canCloseNcr.value) return
  const body: BusinessConsoleNcrCloseRequest = {
    reworkWorkOrderId: optionalText(closeForm.reworkWorkOrderId),
    scrapMovementId: optionalText(closeForm.scrapMovementId),
    returnDocumentId: optionalText(closeForm.returnDocumentId),
  }
  await closeNcr(selectedNcrId.value, body)
  closeSuccess.value = `不合格品 ${selectedNcr.value?.code ?? selectedNcrId.value} 关闭已提交。`
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function splitCsv(value: string) {
  const values = value.split(',').map((item) => item.trim()).filter(Boolean)
  return values.length ? values : undefined
}
function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [
    item.sourceType,
    item.sourceDocumentId,
    item.skuCode,
    item.defectQuantity === undefined || item.defectQuantity === null ? undefined : String(item.defectQuantity),
    item.defectReason,
    item.batchNo,
    item.serialNo,
  ].filter(isPresent)
  return values.length ? values.join(' / ') : '无'
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

watch(detailOpen, (open) => {
  if (!open) selectedNcr.value = undefined
})

// 带 ncrId 进入时使用后端 keyword 定位，避免目标 NCR 因分页、筛选或排序不在当前页。
watch(targetNcrId, (id) => {
  filters.status = undefined
  filters.keyword = id || undefined
  locatedTargetId.value = ''
}, { immediate: true })

// 目标 NCR 出现在结果中即自动打开其处置抽屉；每个目标只自动打开一次（手动关闭后不再弹出）。
watch(targetNcr, (ncr) => {
  if (!ncr || locatedTargetId.value === targetNcrId.value) return
  locatedTargetId.value = targetNcrId.value
  openNcr(ncr)
}, { immediate: true })
</script>

<template>
  <BusinessLayout>
    <PageHeader title="不合格品处理" :breadcrumbs="[{ label: '质量' }]" :count="`${ncrsTotal} 条 NCR`">
      <template #actions>
        <Button v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="ncrsPending" @click="refreshNcrs">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="不合格报告" :value="ncrsTotal" hint="后端筛选总数" />
      <SectionCard description="本页待处理" :value="openCount" hint="当前页未关闭" />
      <SectionCard description="本页已关闭" :value="closedCount" hint="当前页已关闭" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="NCR 状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>
    <p v-else-if="targetNcrMissing" class="text-sm text-warning" role="status">
      未找到 NCR {{ targetNcrId }}。请确认该 NCR 是否已归档或无权访问。
    </p>

    <DataTable
      :columns="columns"
      :rows="ncrs"
      :row-key="(r) => r.id ?? r.code ?? '无'"
      :loading="ncrsPending"
      empty-message="未返回不合格报告。检验不合格或质量阻塞会在这里生成 NCR。"
    >
      <template #cell-code="{ row }">
        <div class="flex flex-col gap-0.5">
          <span class="font-medium">{{ row.code ?? '无' }}</span>
          <span class="text-xs text-muted-foreground">{{ row.id ?? '无 NCR ID' }}</span>
        </div>
      </template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <RowActions :label="`NCR 操作 ${row.code ?? ''}`">
          <DropdownMenuItem @click="openNcr(row)">打开处置</DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="ncrsTotal" />

    <Sheet v-model:open="detailOpen">
      <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{{ selectedNcr?.code ?? '不合格品详情' }}</SheetTitle>
          <SheetDescription>{{ selectedNcr ? qualityItemSummary(selectedNcr) : '查看并提交质量动作。' }}</SheetDescription>
        </SheetHeader>

        <div class="grid gap-4 px-1">
          <div class="grid gap-2 rounded-lg border p-3">
            <div class="flex items-center justify-between gap-2">
              <span class="text-sm font-medium text-foreground">状态</span>
              <StatusBadge :value="selectedNcr?.status" />
            </div>
            <div class="grid gap-1 text-sm text-muted-foreground">
              <span>ID: {{ selectedNcr?.id ?? '无' }}</span>
              <span>编码: {{ selectedNcr?.code ?? '无' }}</span>
            </div>
          </div>

          <form class="grid gap-3 rounded-lg border p-3" @submit.prevent="submitNcrDisposition">
            <h2 class="text-base font-semibold text-foreground">提交处置</h2>
            <p v-if="dispositionErrorMessage" class="text-sm text-destructive" role="alert">{{ dispositionErrorMessage }}</p>
            <p v-if="dispositionSuccess" class="text-sm text-success" role="status">{{ dispositionSuccess }}</p>
            <FieldGroup class="grid gap-3">
              <Field>
                <FieldLabel>处置类型</FieldLabel>
                <Select v-model="dispositionForm.dispositionType">
                  <SelectTrigger aria-label="处置类型"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="use-as-is">让步接收</SelectItem>
                    <SelectItem value="rework">返工</SelectItem>
                    <SelectItem value="scrap">报废</SelectItem>
                    <SelectItem value="return-to-supplier">退供应商</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="ncr-approval-chain">审批链</FieldLabel>
                <Input id="ncr-approval-chain" v-model="dispositionForm.dispositionApprovalChainId" />
              </Field>
              <Field>
                <FieldLabel for="ncr-disposition-files">附件文件 ID</FieldLabel>
                <Input id="ncr-disposition-files" v-model="dispositionForm.attachmentFileIds" placeholder="file-1, file-2" />
              </Field>
            </FieldGroup>
            <div class="flex justify-end">
              <Button type="submit" :disabled="submitDispositionPending || !canSubmitDisposition">
                <Spinner v-if="submitDispositionPending" aria-hidden="true" />
                <SendIcon v-else aria-hidden="true" />
                提交处置
              </Button>
            </div>
          </form>

          <form class="grid gap-3 rounded-lg border p-3" @submit.prevent>
            <h2 class="text-base font-semibold text-foreground">关闭不合格品</h2>
            <p v-if="closeErrorMessage" class="text-sm text-destructive" role="alert">{{ closeErrorMessage }}</p>
            <p v-if="closeSuccess" class="text-sm text-success" role="status">{{ closeSuccess }}</p>
            <FieldGroup class="grid gap-3">
              <Field>
                <FieldLabel for="ncr-rework">返工工单</FieldLabel>
                <Input id="ncr-rework" v-model="closeForm.reworkWorkOrderId" />
              </Field>
              <Field>
                <FieldLabel for="ncr-scrap">报废库存移动</FieldLabel>
                <Input id="ncr-scrap" v-model="closeForm.scrapMovementId" />
              </Field>
              <Field>
                <FieldLabel for="ncr-return">退货单据</FieldLabel>
                <Input id="ncr-return" v-model="closeForm.returnDocumentId" />
              </Field>
            </FieldGroup>

            <SheetFooter>
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button type="button" variant="destructive" :disabled="closeNcrPending || !canCloseNcr">
                    <Spinner v-if="closeNcrPending" aria-hidden="true" />
                    <CheckCircle2Icon v-else aria-hidden="true" />
                    关闭不合格品
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>确认关闭该不合格品？</AlertDialogTitle>
                    <AlertDialogDescription>这里仅提交质量关闭动作，库存、WMS 和 MES 仍按各自服务流程处理。</AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>取消</AlertDialogCancel>
                    <AlertDialogAction @click="submitCloseNcr">确认关闭</AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </SheetFooter>
          </form>
        </div>
      </SheetContent>
    </Sheet>
  </BusinessLayout>
</template>
