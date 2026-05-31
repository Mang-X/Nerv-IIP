<script setup lang="ts">
import BusinessActionSheet from '@/components/business/BusinessActionSheet.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
} from '@nerv-iip/api-client'
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
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, ClipboardPlusIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.counts',
  },
})

const {
  confirmAdjustment,
  confirmAdjustmentError,
  confirmAdjustmentPending,
  createCountTask,
  createCountTaskError,
  createCountTaskPending,
  filters,
} = useInventoryCounts()

const taskSuccess = shallowRef('')
const adjustmentSuccess = shallowRef('')
const taskSheetOpen = shallowRef(false)
const adjustmentSheetOpen = shallowRef(false)
let adjustmentKeySequence = 0

const taskForm = reactive({
  countTaskCode: '',
  skuCode: 'SKU-001',
  uomCode: 'EA',
  siteCode: 'S1',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
})

const adjustmentForm = reactive({
  countTaskId: '',
  countedQuantity: '0',
  idempotencyKey: '',
})

interface CountTaskQueueRow {
  countTaskId: string
  countTaskCode: string
  skuCode: string
  siteCode: string
  locationCode: string
  status: string
  countedQuantity?: number
}

const taskErrorMessage = computed(() => formatError(createCountTaskError.value))
const adjustmentErrorMessage = computed(() => formatError(confirmAdjustmentError.value))
const countTaskQueue = shallowRef<CountTaskQueueRow[]>([])
const selectedCountTask = shallowRef<CountTaskQueueRow>()
const canCreateTask = computed(
  () =>
    isNonEmpty(filters.organizationId) &&
    isNonEmpty(filters.environmentId) &&
    isNonEmpty(taskForm.skuCode) &&
    isNonEmpty(taskForm.uomCode) &&
    isNonEmpty(taskForm.siteCode) &&
    isNonEmpty(taskForm.locationCode),
)
const canConfirmAdjustment = computed(
  () =>
    isNonEmpty(filters.organizationId) &&
    isNonEmpty(filters.environmentId) &&
    isNonEmpty(adjustmentForm.countTaskId) &&
    toOptionalNumber(adjustmentForm.countedQuantity) !== undefined,
)

async function submitTask() {
  if (!canCreateTask.value) return

  const body: BusinessConsoleCreateStockCountTaskRequest = {
    organizationId: filters.organizationId.trim(),
    environmentId: filters.environmentId.trim(),
    countTaskCode: taskForm.countTaskCode.trim() || `COUNT-${Date.now()}`,
    skuCode: taskForm.skuCode.trim(),
    uomCode: taskForm.uomCode.trim(),
    siteCode: taskForm.siteCode.trim(),
    locationCode: taskForm.locationCode.trim(),
    lotNo: optionalText(taskForm.lotNo),
    serialNo: optionalText(taskForm.serialNo),
    qualityStatus: optionalText(taskForm.qualityStatus),
    ownerType: optionalText(taskForm.ownerType),
    ownerId: optionalText(taskForm.ownerId),
  }

  const response = await createCountTask(body)
  const taskId = response?.data?.countTaskId
  taskSuccess.value = `盘点任务 ${taskId ?? body.countTaskCode} 已提交。`
  const row: CountTaskQueueRow = {
    countTaskId: taskId ?? body.countTaskCode ?? '待返回',
    countTaskCode: body.countTaskCode ?? '',
    skuCode: body.skuCode ?? '',
    siteCode: body.siteCode ?? '',
    locationCode: body.locationCode ?? '',
    status: '待实盘',
  }
  countTaskQueue.value = [row, ...countTaskQueue.value]
  taskSheetOpen.value = false
}

async function submitAdjustment() {
  if (!canConfirmAdjustment.value) return

  const body: BusinessConsoleConfirmStockCountAdjustmentRequest = {
    countedQuantity: toOptionalNumber(adjustmentForm.countedQuantity),
    idempotencyKey: optionalText(adjustmentForm.idempotencyKey) ?? createAdjustmentIdempotencyKey(adjustmentForm.countTaskId.trim()),
  }

  const response = await confirmAdjustment(adjustmentForm.countTaskId.trim(), body)
  adjustmentSuccess.value = `库存调整 ${response?.data?.movementId ?? body.idempotencyKey} 已提交。`
  countTaskQueue.value = countTaskQueue.value.map((row) =>
    row.countTaskId === adjustmentForm.countTaskId
      ? { ...row, countedQuantity: body.countedQuantity, status: '已确认' }
      : row,
  )
}

function openAdjustment(row: CountTaskQueueRow) {
  selectedCountTask.value = row
  adjustmentForm.countTaskId = row.countTaskId
  adjustmentForm.countedQuantity = String(row.countedQuantity ?? 0)
  adjustmentForm.idempotencyKey = createAdjustmentIdempotencyKey(row.countTaskId)
  adjustmentSheetOpen.value = true
}

function createAdjustmentIdempotencyKey(countTaskId: string) {
  adjustmentKeySequence += 1
  return `count-${countTaskId}-${Date.now()}-${adjustmentKeySequence}`
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
        domain="库存"
        title="库存盘点"
        summary="以盘点任务为中心安排创建、实盘录入和差异确认。"
      >
        <template #actions>
          <Button size="sm" type="button" @click="taskSheetOpen = true">
            <ClipboardPlusIcon data-icon="inline-start" />
            创建盘点任务
          </Button>
          <Button size="sm" type="button" variant="outline" :disabled="!selectedCountTask" @click="selectedCountTask && openAdjustment(selectedCountTask)">
            <CheckCircle2Icon data-icon="inline-start" />
            {{ selectedCountTask ? '确认差异' : '从任务行确认差异' }}
          </Button>
        </template>
      </BusinessPageHeader>

      <section class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">盘点任务队列</h2>
          <p class="mt-1 text-sm text-muted-foreground">任务创建后进入当前处理队列，差异确认从任务行进入。</p>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>任务号</TableHead>
                <TableHead>物料</TableHead>
                <TableHead>库位</TableHead>
                <TableHead>状态</TableHead>
                <TableHead class="text-right">操作</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in countTaskQueue" :key="row.countTaskId">
                <TableCell class="font-medium text-foreground">{{ row.countTaskId }}</TableCell>
                <TableCell>{{ row.skuCode }}</TableCell>
                <TableCell>{{ row.siteCode }} / {{ row.locationCode }}</TableCell>
                <TableCell>{{ row.status }}</TableCell>
                <TableCell class="text-right">
                  <Button size="sm" type="button" variant="outline" @click="openAdjustment(row)">确认差异</Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
        <BusinessEmptyState
          v-if="!countTaskQueue.length"
          title="暂未接入盘点任务列表"
          description="当前先保留任务创建和差异确认动作，但通过抽屉承载，避免两个表单堆在主页面。"
          action="建议先创建盘点任务，再从任务行进入差异确认。"
        />
      </section>

      <BusinessActionSheet
        v-model:open="taskSheetOpen"
        title="创建盘点任务"
        description="指定物料、工厂、库位和批次后创建盘点任务。"
      >
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitTask">
          <div>
            <p class="text-xs font-bold uppercase text-primary">任务</p>
            <h2 class="text-base font-semibold text-foreground">创建盘点任务</h2>
          </div>

          <BusinessFormStatus :error="taskErrorMessage" :success="taskSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="count-task-sku">SKU</FieldLabel>
              <Input id="count-task-sku" v-model="taskForm.skuCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-uom">单位</FieldLabel>
              <Input id="count-task-uom" v-model="taskForm.uomCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-site">工厂</FieldLabel>
              <Input id="count-task-site" v-model="taskForm.siteCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-location">库位</FieldLabel>
              <Input id="count-task-location" v-model="taskForm.locationCode" required />
            </Field>
            <Field>
              <FieldLabel for="count-task-quality">质量状态</FieldLabel>
              <Input id="count-task-quality" v-model="taskForm.qualityStatus" />
            </Field>
            <Field>
              <FieldLabel for="count-task-owner-type">货主类型</FieldLabel>
              <Input id="count-task-owner-type" v-model="taskForm.ownerType" />
            </Field>
            <Field>
              <FieldLabel for="count-task-owner-id">货主</FieldLabel>
              <Input id="count-task-owner-id" v-model="taskForm.ownerId" placeholder="可选货主名称或编码" />
            </Field>
            <Field>
              <FieldLabel for="count-task-lot">批次</FieldLabel>
              <Input id="count-task-lot" v-model="taskForm.lotNo" />
            </Field>
            <Field>
              <FieldLabel for="count-task-serial">序列号</FieldLabel>
              <Input id="count-task-serial" v-model="taskForm.serialNo" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createCountTaskPending || !canCreateTask">
              <Spinner v-if="createCountTaskPending" data-icon="inline-start" />
              <ClipboardPlusIcon v-else data-icon="inline-start" />
              创建任务
            </Button>
          </div>
        </form>
      </BusinessActionSheet>

      <BusinessActionSheet
        v-model:open="adjustmentSheetOpen"
        title="确认盘点差异"
        description="从已完成实盘的任务进入差异确认，重复提交保护由系统处理。"
      >
        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitAdjustment">
          <div>
            <p class="text-xs font-bold uppercase text-primary">调整</p>
            <h2 class="text-base font-semibold text-foreground">确认盘点差异</h2>
          </div>

          <BusinessFormStatus :error="adjustmentErrorMessage" :success="adjustmentSuccess" />

          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="count-adjust-task-id">盘点任务</FieldLabel>
              <Input id="count-adjust-task-id" v-model="adjustmentForm.countTaskId" readonly required />
            </Field>
            <Field>
              <FieldLabel for="count-adjust-quantity">实盘数量</FieldLabel>
              <Input
                id="count-adjust-quantity"
                v-model="adjustmentForm.countedQuantity"
                inputmode="decimal"
                required
                type="number"
              />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="confirmAdjustmentPending || !canConfirmAdjustment">
              <Spinner v-if="confirmAdjustmentPending" data-icon="inline-start" />
              <CheckCircle2Icon v-else data-icon="inline-start" />
              确认调整
            </Button>
          </div>
        </form>
      </BusinessActionSheet>
    </section>
  </BusinessLayout>
</template>
