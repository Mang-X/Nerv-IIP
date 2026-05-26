<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useInventoryCounts } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleConfirmStockCountAdjustmentRequest,
  BusinessConsoleCreateStockCountTaskRequest,
} from '@nerv-iip/api-client'
import { Button, Field, FieldGroup, FieldLabel, Input, Spinner } from '@nerv-iip/ui'
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

const taskErrorMessage = computed(() => formatError(createCountTaskError.value))
const adjustmentErrorMessage = computed(() => formatError(confirmAdjustmentError.value))
const canCreateTask = computed(
  () =>
    isNonEmpty(filters.organizationId) &&
    isNonEmpty(filters.environmentId) &&
    isNonEmpty(taskForm.countTaskCode) &&
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
    isNonEmpty(adjustmentForm.idempotencyKey) &&
    toOptionalNumber(adjustmentForm.countedQuantity) !== undefined,
)

async function submitTask() {
  if (!canCreateTask.value) return

  const body: BusinessConsoleCreateStockCountTaskRequest = {
    organizationId: filters.organizationId.trim(),
    environmentId: filters.environmentId.trim(),
    countTaskCode: taskForm.countTaskCode.trim(),
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
  if (taskId && !adjustmentForm.countTaskId) {
    adjustmentForm.countTaskId = taskId
  }
}

async function submitAdjustment() {
  if (!canConfirmAdjustment.value) return

  const body: BusinessConsoleConfirmStockCountAdjustmentRequest = {
    countedQuantity: toOptionalNumber(adjustmentForm.countedQuantity),
    idempotencyKey: adjustmentForm.idempotencyKey.trim(),
  }

  const response = await confirmAdjustment(adjustmentForm.countTaskId.trim(), body)
  adjustmentSuccess.value = `库存调整 ${response?.data?.movementId ?? body.idempotencyKey} 已提交。`
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
        summary="创建盘点任务并确认盘点差异，页面不直接耦合库存服务内部实现。"
      />

      <div class="grid gap-3 rounded-lg border bg-background p-4 md:grid-cols-2">
        <Field>
          <FieldLabel for="count-org">组织</FieldLabel>
          <Input id="count-org" v-model="filters.organizationId" />
        </Field>
        <Field>
          <FieldLabel for="count-env">环境</FieldLabel>
          <Input id="count-env" v-model="filters.environmentId" />
        </Field>
      </div>

      <div class="grid gap-4 xl:grid-cols-2">
        <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitTask">
          <div>
            <p class="text-xs font-bold uppercase text-primary">任务</p>
            <h2 class="text-base font-semibold text-foreground">创建盘点任务</h2>
          </div>

          <BusinessFormStatus :error="taskErrorMessage" :success="taskSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="count-task-code">盘点任务编码</FieldLabel>
              <Input id="count-task-code" v-model="taskForm.countTaskCode" required />
            </Field>
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
              <FieldLabel for="count-task-owner-id">货主 ID</FieldLabel>
              <Input id="count-task-owner-id" v-model="taskForm.ownerId" />
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

        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitAdjustment">
          <div>
            <p class="text-xs font-bold uppercase text-primary">调整</p>
            <h2 class="text-base font-semibold text-foreground">确认盘点差异</h2>
          </div>

          <BusinessFormStatus :error="adjustmentErrorMessage" :success="adjustmentSuccess" />

          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="count-adjust-task-id">盘点任务 ID</FieldLabel>
              <Input id="count-adjust-task-id" v-model="adjustmentForm.countTaskId" required />
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
            <Field>
              <FieldLabel for="count-adjust-idempotency">幂等键</FieldLabel>
              <Input id="count-adjust-idempotency" v-model="adjustmentForm.idempotencyKey" required />
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
      </div>
    </section>
  </BusinessLayout>
</template>
