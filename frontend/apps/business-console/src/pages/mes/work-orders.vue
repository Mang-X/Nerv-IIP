<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Checkbox,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, FactoryIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.workOrders',
  },
})

const {
  createRushWorkOrder,
  createRushWorkOrderError,
  createRushWorkOrderPending,
  filters,
  recordProductionReport,
  recordProductionReportError,
  recordProductionReportPending,
  refreshWorkOrders,
  workOrders,
  workOrdersError,
  workOrdersPending,
} = useMesWorkOrders()

const rushSuccess = shallowRef('')
const reportSuccess = shallowRef('')
const lastRushAffectedWorkOrders = shallowRef<string[]>([])
const lastRushScheduleVersion = shallowRef<number>()

const rushForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: 'SKU-001',
  productionVersionId: '',
  quantity: '1',
  dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
  workCenterId: 'WC-001',
  operationTaskId: '',
  operationSequence: '10',
  durationMinutes: '60',
})

const reportForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  operationTaskId: '',
  goodQuantity: '1',
  scrapQuantity: '0',
  completesOperation: true,
  reportedAtUtc: toLocalDateTimeInput(new Date()),
})

const listErrorMessage = computed(() => formatError(workOrdersError.value))
const rushErrorMessage = computed(() => formatError(createRushWorkOrderError.value))
const reportErrorMessage = computed(() => formatError(recordProductionReportError.value))
const reportGoodQuantity = computed(() => toOptionalNumber(reportForm.goodQuantity))
const reportScrapQuantity = computed(() => toOptionalNumber(reportForm.scrapQuantity))
const openOrderCount = computed(
  () => workOrders.value.filter((order) => (order.status ?? '').toLowerCase() !== 'closed').length,
)
const operationCount = computed(() =>
  workOrders.value.reduce((total, order) => total + (order.operationTasks?.length ?? 0), 0),
)
const canCreateRush = computed(
  () =>
    isNonEmpty(rushForm.organizationId) &&
    isNonEmpty(rushForm.environmentId) &&
    isNonEmpty(rushForm.workOrderId) &&
    isNonEmpty(rushForm.skuId) &&
    toOptionalNumber(rushForm.quantity) !== undefined &&
    isNonEmpty(rushForm.dueUtc) &&
    isNonEmpty(rushForm.workCenterId) &&
    toOptionalNumber(rushForm.durationMinutes) !== undefined,
)
const canRecordReport = computed(
  () =>
    isNonEmpty(reportForm.organizationId) &&
    isNonEmpty(reportForm.environmentId) &&
    isNonEmpty(reportForm.workOrderId) &&
    isNonEmpty(reportForm.operationTaskId) &&
    reportGoodQuantity.value !== undefined &&
    reportScrapQuantity.value !== undefined &&
    reportGoodQuantity.value + reportScrapQuantity.value > 0 &&
    isNonEmpty(reportForm.reportedAtUtc),
)

function syncContextFromFilters() {
  rushForm.organizationId = filters.organizationId
  rushForm.environmentId = filters.environmentId
  reportForm.organizationId = filters.organizationId
  reportForm.environmentId = filters.environmentId
}

function useWorkOrder(order: BusinessConsoleMesWorkOrderItem) {
  reportForm.workOrderId = order.workOrderId ?? ''
  const firstTask = order.operationTasks?.[0]
  if (firstTask?.operationTaskId) {
    reportForm.operationTaskId = firstTask.operationTaskId
  }
}

async function submitRushWorkOrder() {
  if (!canCreateRush.value) return

  const body: BusinessConsoleCreateRushWorkOrderRequest = {
    organizationId: rushForm.organizationId.trim(),
    environmentId: rushForm.environmentId.trim(),
    workOrderId: rushForm.workOrderId.trim(),
    skuId: rushForm.skuId.trim(),
    productionVersionId: optionalText(rushForm.productionVersionId),
    quantity: toOptionalNumber(rushForm.quantity),
    dueUtc: toIsoFromLocalInput(rushForm.dueUtc),
    workCenterId: rushForm.workCenterId.trim(),
    operationTaskId: optionalText(rushForm.operationTaskId),
    operationSequence: toOptionalInteger(rushForm.operationSequence),
    durationMinutes: toOptionalInteger(rushForm.durationMinutes),
  }

  const response = await createRushWorkOrder(body)
  rushSuccess.value = `Rush order ${response?.data?.workOrderId ?? body.workOrderId} submitted.`
  lastRushAffectedWorkOrders.value = response?.data?.affectedWorkOrderIds ?? []
  lastRushScheduleVersion.value = response?.data?.schedule?.scheduleVersion
}

async function submitProductionReport() {
  if (!canRecordReport.value) return

  const body: BusinessConsoleRecordProductionReportRequest = {
    organizationId: reportForm.organizationId.trim(),
    environmentId: reportForm.environmentId.trim(),
    workOrderId: reportForm.workOrderId.trim(),
    operationTaskId: reportForm.operationTaskId.trim(),
    goodQuantity: reportGoodQuantity.value,
    scrapQuantity: reportScrapQuantity.value,
    completesOperation: reportForm.completesOperation,
    reportedAtUtc: toIsoFromLocalInput(reportForm.reportedAtUtc),
  }

  const response = await recordProductionReport(body)
  reportSuccess.value = `Production report ${response?.data?.productionReportId ?? body.workOrderId} submitted.`
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function toOptionalInteger(value: string) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : undefined
}

function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}

function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function formatDateTime(value?: string | null) {
  if (!value) return 'n/a'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 3,
  }).format(value ?? 0)
}

function orderKey(order: BusinessConsoleMesWorkOrderItem, index: number) {
  return `${order.workOrderId ?? 'wo'}:${index}`
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? 'Request failed.' : ''
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
        title="Work orders"
        summary="List work orders, create rush work orders, and record production reports."
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="workOrdersPending" @click="refreshWorkOrders">
            <RefreshCwIcon data-icon="inline-start" />
            Refresh
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="work-order-org">Organization</FieldLabel>
            <Input id="work-order-org" v-model="filters.organizationId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="work-order-env">Environment</FieldLabel>
            <Input id="work-order-env" v-model="filters.environmentId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="work-order-status">Status</FieldLabel>
            <Input id="work-order-status" v-model="filters.status" placeholder="optional" />
          </Field>
          <Field>
            <FieldLabel for="work-order-take">Take</FieldLabel>
            <Input id="work-order-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="Work orders" :value="workOrders.length" detail="Returned by BFF" />
        <BusinessMetricCell label="Open orders" :value="openOrderCount" detail="Non-closed status" />
        <BusinessMetricCell label="Operations" :value="operationCount" detail="Visible task rows" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">Work order list</h2>
          <span class="text-sm text-muted-foreground">{{ workOrders.length }} returned</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Work order</TableHead>
                <TableHead>Status</TableHead>
                <TableHead class="text-right">Quantity</TableHead>
                <TableHead>Due</TableHead>
                <TableHead>Operations</TableHead>
                <TableHead class="text-right">Report</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(order, index) in workOrders" :key="orderKey(order, index)">
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <span class="font-medium">{{ order.workOrderId ?? 'n/a' }}</span>
                    <span class="text-xs text-muted-foreground">{{ order.skuId ?? 'No SKU' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ order.status ?? 'unknown' }}</Badge>
                </TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(order.quantity) }}</TableCell>
                <TableCell>{{ formatDateTime(order.dueUtc) }}</TableCell>
                <TableCell>
                  <div class="grid gap-1">
                    <span
                      v-for="task in order.operationTasks ?? []"
                      :key="task.operationTaskId ?? `${order.workOrderId}-${task.operationSequence}`"
                      class="text-xs text-muted-foreground"
                    >
                      {{ task.operationSequence ?? 'n/a' }} /
                      {{ task.workCenterId ?? 'n/a' }} /
                      {{ task.status ?? 'unknown' }}
                    </span>
                    <span v-if="!(order.operationTasks?.length)" class="text-xs text-muted-foreground">
                      No task rows
                    </span>
                  </div>
                </TableCell>
                <TableCell class="text-right">
                  <Button size="sm" variant="outline" type="button" @click="useWorkOrder(order)">
                    Use
                  </Button>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!workOrders.length && !workOrdersPending" :colspan="6">
                No work orders returned.
              </TableEmpty>
              <TableEmpty v-if="workOrdersPending" :colspan="6">Loading work orders...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <div class="grid gap-4 xl:grid-cols-2">
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitRushWorkOrder">
          <div>
            <p class="text-xs font-bold uppercase text-primary">Rush</p>
            <h2 class="text-base font-semibold text-foreground">Create rush work order</h2>
          </div>
          <BusinessFormStatus :error="rushErrorMessage" :success="rushSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="rush-org">Organization</FieldLabel>
              <Input id="rush-org" v-model="rushForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-env">Environment</FieldLabel>
              <Input id="rush-env" v-model="rushForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-work-order">Work order ID</FieldLabel>
              <Input id="rush-work-order" v-model="rushForm.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-sku">SKU ID</FieldLabel>
              <Input id="rush-sku" v-model="rushForm.skuId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-version">Production version</FieldLabel>
              <Input id="rush-version" v-model="rushForm.productionVersionId" />
            </Field>
            <Field>
              <FieldLabel for="rush-quantity">Quantity</FieldLabel>
              <Input id="rush-quantity" v-model="rushForm.quantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="rush-due">Due</FieldLabel>
              <Input id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
            </Field>
            <Field>
              <FieldLabel for="rush-work-center">Work center</FieldLabel>
              <Input id="rush-work-center" v-model="rushForm.workCenterId" required />
            </Field>
            <Field>
              <FieldLabel for="rush-operation-task">Operation task</FieldLabel>
              <Input id="rush-operation-task" v-model="rushForm.operationTaskId" />
            </Field>
            <Field>
              <FieldLabel for="rush-operation-sequence">Operation sequence</FieldLabel>
              <Input id="rush-operation-sequence" v-model="rushForm.operationSequence" inputmode="numeric" type="number" />
            </Field>
            <Field>
              <FieldLabel for="rush-duration">Duration minutes</FieldLabel>
              <Input id="rush-duration" v-model="rushForm.durationMinutes" inputmode="numeric" required type="number" />
            </Field>
          </FieldGroup>

          <div v-if="lastRushScheduleVersion || lastRushAffectedWorkOrders.length" class="grid gap-2 rounded-lg border p-3">
            <p class="text-sm font-semibold text-foreground">Generated schedule visibility</p>
            <p v-if="lastRushScheduleVersion" class="text-sm text-muted-foreground">
              Schedule version {{ lastRushScheduleVersion }}
            </p>
            <p v-if="lastRushAffectedWorkOrders.length" class="text-sm text-muted-foreground">
              Affected work orders: {{ lastRushAffectedWorkOrders.join(', ') }}
            </p>
          </div>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
              <Spinner v-if="createRushWorkOrderPending" data-icon="inline-start" />
              <FactoryIcon v-else data-icon="inline-start" />
              Create rush order
            </Button>
          </div>
        </form>

        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitProductionReport">
          <div>
            <p class="text-xs font-bold uppercase text-primary">Report</p>
            <h2 class="text-base font-semibold text-foreground">Record production report</h2>
          </div>
          <BusinessFormStatus :error="reportErrorMessage" :success="reportSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="report-org">Organization</FieldLabel>
              <Input id="report-org" v-model="reportForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="report-env">Environment</FieldLabel>
              <Input id="report-env" v-model="reportForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="report-work-order">Work order ID</FieldLabel>
              <Input id="report-work-order" v-model="reportForm.workOrderId" required />
            </Field>
            <Field>
              <FieldLabel for="report-operation-task">Operation task ID</FieldLabel>
              <Input id="report-operation-task" v-model="reportForm.operationTaskId" required />
            </Field>
            <Field>
              <FieldLabel for="report-good">Good quantity</FieldLabel>
              <Input id="report-good" v-model="reportForm.goodQuantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="report-scrap">Scrap quantity</FieldLabel>
              <Input id="report-scrap" v-model="reportForm.scrapQuantity" inputmode="decimal" required type="number" />
            </Field>
            <Field>
              <FieldLabel for="report-time">Reported at</FieldLabel>
              <Input id="report-time" v-model="reportForm.reportedAtUtc" required type="datetime-local" />
            </Field>
            <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
              <FieldLabel for="report-complete">Completes operation</FieldLabel>
              <Checkbox id="report-complete" v-model:checked="reportForm.completesOperation" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="recordProductionReportPending || !canRecordReport">
              <Spinner v-if="recordProductionReportPending" data-icon="inline-start" />
              <ClipboardCheckIcon v-else data-icon="inline-start" />
              Record report
            </Button>
          </div>
        </form>
      </div>
    </section>
  </BusinessLayout>
</template>
