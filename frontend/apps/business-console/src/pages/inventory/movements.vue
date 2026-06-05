<script setup lang="ts">
import type { BusinessConsolePostStockMovementRequest } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useInventoryMovement } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '库存移动过账' } })

const route = useRoute()
const { postMovement, postMovementError, postMovementPending } = useInventoryMovement()

const form = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  movementType: 'receipt',
  sourceService: 'business-console',
  sourceDocumentId: '',
  sourceDocumentLineId: '',
  idempotencyKey: '',
  skuCode: 'SKU-001',
  uomCode: 'EA',
  siteCode: 'S1',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
  quantity: '1',
})

interface MovementQueueRow {
  movementId: string
  movementType: string
  skuCode: string
  siteCode: string
  locationCode: string
  quantity: number
  status: string
  sourceDocumentId: string
}

const successMessage = shallowRef('')
const movementSheetOpen = shallowRef(false)
const movementQueue = shallowRef<MovementQueueRow[]>([])
const errorMessage = computed(() => formatError(postMovementError.value))

// 上下文穿透：从来源单据（收货/完工入库/领料/盘点）带入 SKU/库位/批次。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
watch(
  () => route.query,
  (query) => {
    const source = firstQuery(query.sourceDocumentId) || firstQuery(query.workOrderId)
    if (source) form.sourceDocumentId = source
    const sku = firstQuery(query.skuCode) || firstQuery(query.skuId)
    if (sku) form.skuCode = sku
    const site = firstQuery(query.siteCode)
    if (site) form.siteCode = site
    const location = firstQuery(query.locationCode)
    if (location) form.locationCode = location
    const lot = firstQuery(query.lotNo) || firstQuery(query.materialLotId)
    if (lot) form.lotNo = lot
    const serial = firstQuery(query.serialNo)
    if (serial) form.serialNo = serial
  },
  { immediate: true },
)

const stableSubmissionKey = computed(() =>
  [form.movementType, form.sourceDocumentId, form.sourceDocumentLineId, form.skuCode, form.siteCode, form.locationCode, form.quantity]
    .map((part) => String(part || '').trim() || 'none')
    .join('|'),
)
const canSubmit = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.movementType) &&
    isNonEmpty(form.sourceDocumentId) &&
    isNonEmpty(form.skuCode) &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.siteCode) &&
    isNonEmpty(form.locationCode) &&
    toOptionalNumber(form.quantity) !== undefined,
)

type QueueRow = MovementQueueRow
const columns: DataTableColumn<QueueRow>[] = [
  { key: 'movementId', header: '移动号', cellClass: 'font-medium' },
  { key: 'movementType', header: '类型' },
  { key: 'skuCode', header: '物料' },
  { key: 'location', header: '库位', accessor: (r) => `${r.siteCode} / ${r.locationCode}` },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-24' },
  { key: 'status', header: '状态', width: 'w-24' },
]

async function submitMovement() {
  if (!canSubmit.value) return
  const body: BusinessConsolePostStockMovementRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    movementType: form.movementType,
    sourceService: form.sourceService.trim() || 'business-console',
    sourceDocumentId: form.sourceDocumentId.trim(),
    sourceDocumentLineId: optionalText(form.sourceDocumentLineId),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `movement-${stableSubmissionKey.value}`,
    skuCode: form.skuCode.trim(),
    uomCode: form.uomCode.trim(),
    siteCode: form.siteCode.trim(),
    locationCode: form.locationCode.trim(),
    lotNo: optionalText(form.lotNo),
    serialNo: optionalText(form.serialNo),
    qualityStatus: form.qualityStatus.trim(),
    ownerType: form.ownerType.trim(),
    ownerId: optionalText(form.ownerId),
    quantity: toOptionalNumber(form.quantity),
  }
  const response = await postMovement(body)
  successMessage.value = `库存移动 ${response?.data?.movementId ?? body.idempotencyKey} 已受理。`
  movementQueue.value = [
    {
      movementId: response?.data?.movementId ?? body.sourceDocumentId ?? '待返回',
      movementType: body.movementType ?? '',
      skuCode: body.skuCode ?? '',
      siteCode: body.siteCode ?? '',
      locationCode: body.locationCode ?? '',
      quantity: body.quantity ?? 0,
      status: '已受理',
      sourceDocumentId: body.sourceDocumentId ?? '',
    },
    ...movementQueue.value,
  ]
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
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
</script>

<template>
  <BusinessLayout>
    <PageHeader title="库存移动过账" :breadcrumbs="[{ label: '库存' }]" :count="`${movementQueue.length} 条本次受理`">
      <template #actions>
        <Button v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </Button>
        <Button size="sm" type="button" @click="movementSheetOpen = true">
          <SendIcon aria-hidden="true" />
          新建移动
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      常规业务应从收货、完工入库、领料或盘点单据自动发起；移动来源由业务单据带出，重复提交保护由系统处理。提交后回到库存可用量确认影响。
    </p>

    <DataTable
      :columns="columns"
      :rows="movementQueue"
      row-key="movementId"
      empty-message="当前没有待确认库存移动。建议从收货、完工入库、领料或盘点任务发起；确需补录时点右上角新建移动。"
    >
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ row.quantity }}</span></template>
    </DataTable>

    <Dialog v-model:open="movementSheetOpen">
      <DialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>新建库存移动</DialogTitle>
          <DialogDescription>用于少量人工补录和异常处理；常规业务应从来源单据自动发起。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitMovement">
          <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
          <p v-if="successMessage" class="text-sm text-success" role="status">{{ successMessage }}</p>

          <FieldGroup class="grid gap-3 md:grid-cols-3">
            <Field>
              <FieldLabel>移动类型</FieldLabel>
              <Select v-model="form.movementType">
                <SelectTrigger aria-label="移动类型"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="receipt">入库</SelectItem>
                  <SelectItem value="issue">出库</SelectItem>
                  <SelectItem value="transfer">调拨</SelectItem>
                  <SelectItem value="adjustment">调整</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="movement-source-document">来源单据</FieldLabel>
              <Input id="movement-source-document" v-model="form.sourceDocumentId" required />
            </Field>
            <Field>
              <FieldLabel for="movement-source-line">来源行</FieldLabel>
              <Input id="movement-source-line" v-model="form.sourceDocumentLineId" />
            </Field>
            <Field>
              <FieldLabel for="movement-sku">SKU</FieldLabel>
              <Input id="movement-sku" v-model="form.skuCode" required />
            </Field>
            <Field>
              <FieldLabel for="movement-uom">单位</FieldLabel>
              <Input id="movement-uom" v-model="form.uomCode" required />
            </Field>
            <Field>
              <FieldLabel for="movement-site">工厂</FieldLabel>
              <Input id="movement-site" v-model="form.siteCode" required />
            </Field>
            <Field>
              <FieldLabel for="movement-location">库位</FieldLabel>
              <Input id="movement-location" v-model="form.locationCode" required />
            </Field>
            <Field>
              <FieldLabel for="movement-quantity">数量</FieldLabel>
              <Input id="movement-quantity" v-model="form.quantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="movement-quality">质量状态</FieldLabel>
              <Input id="movement-quality" v-model="form.qualityStatus" required />
            </Field>
            <Field>
              <FieldLabel for="movement-owner-id">货主</FieldLabel>
              <Input id="movement-owner-id" v-model="form.ownerId" placeholder="可选货主名称或编码" />
            </Field>
            <Field>
              <FieldLabel for="movement-lot">批次</FieldLabel>
              <Input id="movement-lot" v-model="form.lotNo" />
            </Field>
            <Field>
              <FieldLabel for="movement-serial">序列号</FieldLabel>
              <Input id="movement-serial" v-model="form.serialNo" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="postMovementPending || !canSubmit">
              <Spinner v-if="postMovementPending" aria-hidden="true" />
              <SendIcon v-else aria-hidden="true" />
              提交库存移动
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
