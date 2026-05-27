<script setup lang="ts">
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessRowActions from '@/components/business/BusinessRowActions.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleNcrCloseRequest,
  BusinessConsoleNcrDispositionRequest,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
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
  DropdownMenuItem,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
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
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.ncrs',
  },
})

const {
  closeNcr,
  closeNcrError,
  closeNcrPending,
  filters,
  ncrs,
  ncrsError,
  ncrsPending,
  refreshNcrs,
  submitDisposition,
  submitDispositionError,
  submitDispositionPending,
} = useQualityNcrs()

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

const listErrorMessage = computed(() => formatError(ncrsError.value))
const dispositionErrorMessage = computed(() => formatError(submitDispositionError.value))
const closeErrorMessage = computed(() => formatError(closeNcrError.value))
const selectedNcrId = computed(() => selectedNcr.value?.id ?? '')
const canSubmitDisposition = computed(
  () => isNonEmpty(selectedNcrId.value) && isNonEmpty(dispositionForm.dispositionType),
)
const canCloseNcr = computed(() => isNonEmpty(selectedNcrId.value))
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})

function openNcr(ncr: BusinessConsoleQualityItem) {
  selectedNcr.value = ncr
  dispositionSuccess.value = ''
  closeSuccess.value = ''
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
  const values = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)

  return values.length ? values : undefined
}

function rowKey(item: BusinessConsoleQualityItem, index: number) {
  return `${item.id ?? item.code ?? 'ncr'}:${index}`
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

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}

function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="质量"
        title="不合格品处理"
        summary="查看不合格报告，并通过质量服务提交处置或关闭动作。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="ncrsPending" @click="refreshNcrs">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessContextBar
        v-model:environment-id="filters.environmentId"
        v-model:organization-id="filters.organizationId"
        :show-line="false"
        :show-shift="false"
        :show-site="false"
        :show-work-center="false"
        title="质量信息"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-2">
          <Field>
            <FieldLabel for="ncr-status">状态</FieldLabel>
            <Select v-model="statusFilter">
              <SelectTrigger id="ncr-status" aria-label="NCR 状态">
                <SelectValue placeholder="全部状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="ncr-take">返回条数</FieldLabel>
            <Input id="ncr-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </BusinessContextBar>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">不合格报告</h2>
          <span class="text-sm text-muted-foreground">返回 {{ ncrs.length }} 条</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>NCR</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>摘要</TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(ncr, index) in ncrs" :key="rowKey(ncr, index)">
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <span class="font-medium">{{ ncr.code ?? '无' }}</span>
                    <span class="text-xs text-muted-foreground">{{ ncr.id ?? '无 NCR ID' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <BusinessStatusBadge :value="ncr.status" />
                </TableCell>
                <TableCell>{{ qualityItemSummary(ncr) }}</TableCell>
                <TableCell class="text-right">
                  <BusinessRowActions :label="`NCR 操作 ${ncr.code ?? ''}`">
                    <DropdownMenuItem @click="openNcr(ncr)">打开处置</DropdownMenuItem>
                  </BusinessRowActions>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!ncrs.length && !ncrsPending" :colspan="4">
                未返回不合格报告。
              </TableEmpty>
              <TableEmpty v-if="ncrsPending" :colspan="4">正在加载不合格报告...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <Sheet v-model:open="detailOpen">
        <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
          <SheetHeader>
            <SheetTitle>{{ selectedNcr?.code ?? '不合格品详情' }}</SheetTitle>
            <SheetDescription>
              {{ selectedNcr ? qualityItemSummary(selectedNcr) : '查看并提交质量动作。' }}
            </SheetDescription>
          </SheetHeader>

          <div class="grid gap-4 px-1">
            <div class="grid gap-2 rounded-lg border p-3">
              <div class="flex items-center justify-between gap-2">
                <span class="text-sm font-medium text-foreground">状态</span>
                <BusinessStatusBadge :value="selectedNcr?.status" />
              </div>
              <div class="grid gap-1 text-sm text-muted-foreground">
                <span>ID: {{ selectedNcr?.id ?? '无' }}</span>
                <span>编码: {{ selectedNcr?.code ?? '无' }}</span>
              </div>
            </div>

            <form class="grid gap-3 rounded-lg border p-3" @submit.prevent="submitNcrDisposition">
              <div>
                <p class="text-xs font-bold uppercase text-primary">处置</p>
                <h2 class="text-base font-semibold text-foreground">提交处置</h2>
              </div>
              <BusinessFormStatus :error="dispositionErrorMessage" :success="dispositionSuccess" />
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel>处置类型</FieldLabel>
                  <Select v-model="dispositionForm.dispositionType">
                    <SelectTrigger aria-label="处置类型">
                      <SelectValue />
                    </SelectTrigger>
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
                  <Spinner v-if="submitDispositionPending" data-icon="inline-start" />
                  <SendIcon v-else data-icon="inline-start" />
                  提交处置
                </Button>
              </div>
            </form>

            <form class="grid gap-3 rounded-lg border p-3" @submit.prevent>
              <div>
                <p class="text-xs font-bold uppercase text-primary">关闭</p>
                <h2 class="text-base font-semibold text-foreground">关闭不合格品</h2>
              </div>
              <BusinessFormStatus :error="closeErrorMessage" :success="closeSuccess" />
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
                    <Button
                      type="button"
                      variant="destructive"
                      :disabled="closeNcrPending || !canCloseNcr"
                    >
                      <Spinner v-if="closeNcrPending" data-icon="inline-start" />
                      <CheckCircle2Icon v-else data-icon="inline-start" />
                      关闭不合格品
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>确认关闭该不合格品？</AlertDialogTitle>
                      <AlertDialogDescription>
                        这里仅提交质量关闭动作，库存、WMS 和 MES 仍按各自服务流程处理。
                      </AlertDialogDescription>
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
    </section>
  </BusinessLayout>
</template>
