<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useMesFinishedGoodsReceipts } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsoleMesCreateReceiptRequest } from '@nerv-iip/api-client'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { PackageCheckIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '完工入库',
  },
})

const {
  createReceiptRequest,
  createReceiptRequestError,
  createReceiptRequestPending,
  filters,
  receiptRequests,
  receiptRequestsError,
  receiptRequestsPending,
  refreshReceiptRequests,
} = useMesFinishedGoodsReceipts()

const successMessage = shallowRef('')
const receiptSheetOpen = shallowRef(false)
const form = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: 'SKU-001',
  quantity: '1',
  uomCode: 'EA',
  requestedAtUtc: toLocalDateTimeInput(new Date()),
  idempotencyKey: '',
})

const listErrorMessage = computed(() => formatError(receiptRequestsError.value))
const createErrorMessage = computed(() => formatError(createReceiptRequestError.value))
const canCreate = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.workOrderId) &&
    isNonEmpty(form.skuId) &&
    toOptionalNumber(form.quantity) !== undefined &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.requestedAtUtc),
)
const pendingCount = computed(
  () => receiptRequests.value.filter((item) => item.receiptStatus !== 'Completed').length,
)

function syncContextFromFilters() {
  form.organizationId = filters.organizationId
  form.environmentId = filters.environmentId
}

async function submitReceiptRequest() {
  if (!canCreate.value) return

  const body: BusinessConsoleMesCreateReceiptRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    workOrderId: form.workOrderId.trim(),
    skuId: form.skuId.trim(),
    quantity: toOptionalNumber(form.quantity),
    uomCode: form.uomCode.trim(),
    requestedAtUtc: toIsoFromLocalInput(form.requestedAtUtc),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `receipt-${form.workOrderId.trim()}`,
  }

  await createReceiptRequest(body)
  successMessage.value = `完工入库请求 ${body.workOrderId} 已提交。`
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}

function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="完工入库"
        summary="查看完工入库请求；新增入库尽量从报工完成、工单详情或质量放行后触发。"
      >
        <template #actions>
          <Button size="sm" type="button" @click="receiptSheetOpen = true">
            <PackageCheckIcon data-icon="inline-start" />
            新增入库请求
          </Button>
          <Button size="sm" type="button" variant="outline" :disabled="receiptRequestsPending" @click="refreshReceiptRequests">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-4">
        <div class="grid gap-4">
          <div class="grid gap-3 rounded-lg border bg-background p-4">
            <FieldGroup class="grid gap-3 md:grid-cols-4">
              <Field>
                <FieldLabel for="receipt-list-org">组织</FieldLabel>
                <Input id="receipt-list-org" v-model="filters.organizationId" @change="syncContextFromFilters" />
              </Field>
              <Field>
                <FieldLabel for="receipt-list-env">环境</FieldLabel>
                <Input id="receipt-list-env" v-model="filters.environmentId" @change="syncContextFromFilters" />
              </Field>
              <Field>
                <FieldLabel for="receipt-list-status">状态</FieldLabel>
                <Input id="receipt-list-status" v-model="filters.status" placeholder="可选" />
              </Field>
              <Field>
                <FieldLabel for="receipt-list-take">数量</FieldLabel>
                <Input id="receipt-list-take" v-model.number="filters.take" inputmode="numeric" type="number" />
              </Field>
            </FieldGroup>
            <BusinessFormStatus :error="listErrorMessage" />
          </div>

          <div class="grid gap-3 md:grid-cols-3">
            <BusinessMetricCell label="入库请求" :value="receiptRequests.length" detail="当前筛选结果" />
            <BusinessMetricCell label="待处理" :value="pendingCount" detail="未完成请求" />
            <BusinessMetricCell label="已完成" :value="receiptRequests.length - pendingCount" detail="Completed 状态" />
          </div>

          <div class="overflow-hidden rounded-lg border bg-background">
            <div class="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>请求号</TableHead>
                    <TableHead>工单</TableHead>
                    <TableHead>SKU</TableHead>
                    <TableHead class="text-right">数量</TableHead>
                    <TableHead>状态</TableHead>
                    <TableHead>请求时间</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="row in receiptRequests" :key="row.receiptRequestId">
                    <TableCell class="font-medium">{{ row.receiptRequestId ?? '无' }}</TableCell>
                    <TableCell>{{ row.workOrderId ?? '无' }}</TableCell>
                    <TableCell>{{ row.skuId ?? '无' }}</TableCell>
                    <TableCell class="text-right tabular-nums">{{ formatQuantity(row.quantity) }}</TableCell>
                    <TableCell>
                      <BusinessStatusBadge :value="row.receiptStatus" />
                    </TableCell>
                    <TableCell>{{ formatDateTime(row.requestedAtUtc) }}</TableCell>
                  </TableRow>
                  <TableEmpty v-if="!receiptRequests.length && !receiptRequestsPending" :colspan="6">
                    <BusinessEmptyState
                      title="当前没有完工入库请求"
                      description="完工入库通常从生产报工完成、质量放行或工单详情中发起。"
                      action="需要临时补录时，使用右上角新增入库请求。"
                    />
                  </TableEmpty>
                  <TableEmpty v-if="receiptRequestsPending" :colspan="6">正在加载完工入库...</TableEmpty>
                </TableBody>
              </Table>
            </div>
          </div>
        </div>
      </div>

      <BusinessActionSheet
        v-model:open="receiptSheetOpen"
        title="新增入库请求"
        description="用于生产完成后的成品入库申请，常规场景应从工单或报工上下文带出字段。"
      >
        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitReceiptRequest">
          <div>
            <p class="text-xs font-bold uppercase text-primary">入库</p>
            <h2 class="text-base font-semibold text-foreground">新增入库请求</h2>
          </div>
          <BusinessFormStatus :error="createErrorMessage" :success="successMessage" />

          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="receipt-org">组织</FieldLabel>
              <Input id="receipt-org" v-model="form.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-env">环境</FieldLabel>
              <Input id="receipt-env" v-model="form.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-work-order">工单号</FieldLabel>
              <Input id="receipt-work-order" v-model="form.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-sku">SKU</FieldLabel>
              <Input id="receipt-sku" v-model="form.skuId" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-quantity">数量</FieldLabel>
              <Input id="receipt-quantity" v-model="form.quantity" inputmode="decimal" min="0" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="receipt-uom">单位</FieldLabel>
              <Input id="receipt-uom" v-model="form.uomCode" required />
            </Field>
            <Field>
              <FieldLabel for="receipt-requested-at">请求时间</FieldLabel>
              <Input id="receipt-requested-at" v-model="form.requestedAtUtc" required type="datetime-local" />
            </Field>
            <Field>
              <FieldLabel for="receipt-idempotency">幂等键</FieldLabel>
              <Input id="receipt-idempotency" v-model="form.idempotencyKey" placeholder="默认按工单生成" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createReceiptRequestPending || !canCreate">
              <Spinner v-if="createReceiptRequestPending" data-icon="inline-start" />
              <PackageCheckIcon v-else data-icon="inline-start" />
              提交入库请求
            </Button>
          </div>
        </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
