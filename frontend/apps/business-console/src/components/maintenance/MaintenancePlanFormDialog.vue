<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenancePlanRequest,
  BusinessConsoleMaintenancePlanItem,
  BusinessConsoleUpdateMaintenancePlanRequest,
} from '@nerv-iip/api-client'
import {
  NvButton,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef, watch } from 'vue'

type TriggerMode = 'calendar' | 'runtime' | 'both'
type DialogMode = 'create' | 'edit'

interface PlanFormState {
  deviceAssetId: string
  planCode: string
  triggerMode: TriggerMode
  interval: string
  runtimeHourInterval: string | number
  startsOn: string
  owner: string
}

interface Props {
  mode: DialogMode
  organizationId: string
  environmentId: string
  plan?: BusinessConsoleMaintenancePlanItem
  pending?: boolean
}

interface CreateSubmission {
  mode: 'create'
  body: BusinessConsoleCreateMaintenancePlanRequest
}

interface UpdateSubmission {
  mode: 'edit'
  planId: string
  body: BusinessConsoleUpdateMaintenancePlanRequest
}

type MaintenancePlanFormSubmission = CreateSubmission | UpdateSubmission

const props = defineProps<Props>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{
  submit: [submission: MaintenancePlanFormSubmission]
}>()

const intervalOptions = [
  { label: '每周', value: 'P7D' },
  { label: '每两周', value: 'P14D' },
  { label: '每月', value: 'P30D' },
  { label: '每季度', value: 'P90D' },
]
const runtimeHourQuickValues = [500, 1000, 2000]

const form = reactive<PlanFormState>({
  deviceAssetId: '',
  planCode: '',
  triggerMode: 'calendar' as TriggerMode,
  interval: 'P30D',
  runtimeHourInterval: '',
  startsOn: '',
  owner: '',
})
const submitted = shallowRef(false)

const isEditMode = computed(() => props.mode === 'edit')
const usesCalendar = computed(() => form.triggerMode !== 'runtime')
const usesRuntime = computed(() => form.triggerMode !== 'calendar')
const deviceInvalid = computed(
  () => submitted.value && !isEditMode.value && !form.deviceAssetId.trim(),
)
const ownerInvalid = computed(() => submitted.value && !isEditMode.value && !form.owner.trim())
const runtimeHoursInvalid = computed(() => {
  if (!submitted.value || !usesRuntime.value) return false
  const runtimeHoursText = String(form.runtimeHourInterval).trim()
  const parsed = Number(runtimeHoursText)
  return !runtimeHoursText || !Number.isFinite(parsed) || parsed <= 0
})
const validationMessage = computed(() => {
  if (runtimeHoursInvalid.value) return '请填写正的触发运行小时数。'
  if (deviceInvalid.value || ownerInvalid.value) return '请完善标红的必填项。'
  return ''
})

function todayDate() {
  return new Date().toISOString().slice(0, 10)
}

function triggerModeFor(plan?: BusinessConsoleMaintenancePlanItem): TriggerMode {
  const hasCalendar = !!plan?.interval
  const hasRuntime = plan?.runtimeHourInterval != null
  if (hasCalendar && hasRuntime) return 'both'
  if (hasRuntime) return 'runtime'
  return 'calendar'
}

function resetForm() {
  submitted.value = false
  if (props.mode === 'edit' && props.plan) {
    form.deviceAssetId = props.plan.deviceAssetId ?? ''
    form.planCode = props.plan.planCode ?? ''
    form.triggerMode = triggerModeFor(props.plan)
    form.interval = props.plan.interval ?? 'P30D'
    form.runtimeHourInterval =
      props.plan.runtimeHourInterval == null ? '' : String(props.plan.runtimeHourInterval)
    form.startsOn = props.plan.startsOn ?? ''
    form.owner = ''
    return
  }

  form.deviceAssetId = ''
  form.planCode = ''
  form.triggerMode = 'calendar'
  form.interval = 'P30D'
  form.runtimeHourInterval = ''
  form.startsOn = todayDate()
  form.owner = ''
}

watch(
  () => [open.value, props.mode, props.plan] as const,
  ([isOpen]) => {
    if (isOpen) resetForm()
  },
  { immediate: true },
)

function setRuntimeHours(value: number) {
  form.runtimeHourInterval = String(value)
}

function submitForm() {
  submitted.value = true
  if (deviceInvalid.value || ownerInvalid.value || runtimeHoursInvalid.value) return

  const runtimeHourInterval = usesRuntime.value ? Number(form.runtimeHourInterval) : undefined
  if (props.mode === 'edit') {
    const planId = props.plan?.planId
    if (!planId) return
    emit('submit', {
      mode: 'edit',
      planId,
      body: {
        organizationId: props.organizationId,
        environmentId: props.environmentId,
        interval: usesCalendar.value ? form.interval : null,
        runtimeHourInterval: usesRuntime.value ? runtimeHourInterval : null,
      },
    })
    return
  }

  emit('submit', {
    mode: 'create',
    body: {
      organizationId: props.organizationId,
      environmentId: props.environmentId,
      deviceAssetId: form.deviceAssetId.trim(),
      planCode: form.planCode.trim() || undefined,
      interval: usesCalendar.value ? form.interval : undefined,
      startsOn: form.startsOn || todayDate(),
      owner: form.owner.trim(),
      runtimeHourInterval,
    },
  })
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>{{ isEditMode ? '编辑保养计划' : '新建保养计划' }}</NvDialogTitle>
        <NvDialogDescription v-if="isEditMode">
          调整未来的触发条件；保存后系统会重算后续到期点，不影响已经生成的维护工单。
        </NvDialogDescription>
        <NvDialogDescription v-else>
          为设备登记周期保养，系统据此推算到期并批量生成维护工单。
        </NvDialogDescription>
      </NvDialogHeader>

      <form class="grid gap-4" @submit.prevent="submitForm">
        <NvField>
          <NvFieldLabel>触发模式</NvFieldLabel>
          <NvTabs v-model="form.triggerMode">
            <NvTabsList class="w-full">
              <NvTabsTrigger value="calendar" class="flex-1">日历周期</NvTabsTrigger>
              <NvTabsTrigger value="runtime" class="flex-1">运行小时</NvTabsTrigger>
              <NvTabsTrigger value="both" class="flex-1">两者组合</NvTabsTrigger>
            </NvTabsList>
          </NvTabs>
          <p class="text-xs text-muted-foreground">
            <template v-if="form.triggerMode === 'calendar'">
              按保养周期到期开单，例如每月一次。
            </template>
            <template v-else-if="form.triggerMode === 'runtime'">
              按设备累计运行小时到期开单，不受日历影响；例如每运行满 1000 小时保养一次。
            </template>
            <template v-else>
              同时保留日历周期与运行小时两条到期线，两条线各自到期、各自开单。
            </template>
          </p>
        </NvField>

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="plan-device">设备</NvFieldLabel>
            <NvInput
              id="plan-device"
              v-model="form.deviceAssetId"
              autocomplete="off"
              placeholder="如 DEV-SMT-01"
              :readonly="isEditMode"
              :invalid="deviceInvalid"
            />
            <NvFieldError v-if="deviceInvalid" :errors="['请选择或填写设备。']" />
          </NvField>

          <NvField>
            <NvFieldLabel for="plan-code">计划编号</NvFieldLabel>
            <NvInput
              id="plan-code"
              v-model="form.planCode"
              autocomplete="off"
              placeholder="可选，如 PM-SMT-01-M"
              :readonly="isEditMode"
            />
          </NvField>

          <NvField v-if="usesCalendar">
            <NvFieldLabel for="plan-interval">保养周期</NvFieldLabel>
            <NvSelect v-model="form.interval">
              <NvSelectTrigger id="plan-interval" aria-label="保养周期">
                <NvSelectValue />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in intervalOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>

          <NvField v-if="usesRuntime">
            <NvFieldLabel for="plan-runtime-hours">触发运行小时</NvFieldLabel>
            <NvInput
              id="plan-runtime-hours"
              v-model="form.runtimeHourInterval"
              type="number"
              inputmode="numeric"
              min="1"
              step="1"
              autocomplete="off"
              placeholder="如 1000"
              :invalid="runtimeHoursInvalid"
              aria-describedby="plan-runtime-hours-error"
            />
            <NvFieldError
              v-if="runtimeHoursInvalid"
              id="plan-runtime-hours-error"
              :errors="['请填写正的触发运行小时数。']"
            />
            <div class="flex flex-wrap gap-2 pt-1">
              <NvButton
                v-for="value in runtimeHourQuickValues"
                :key="value"
                size="sm"
                type="button"
                variant="outline"
                @click="setRuntimeHours(value)"
              >
                {{ value }} 小时
              </NvButton>
            </div>
          </NvField>

          <NvField>
            <NvFieldLabel for="plan-starts">起始日期</NvFieldLabel>
            <NvInput id="plan-starts" v-model="form.startsOn" type="date" :readonly="isEditMode" />
          </NvField>

          <NvField v-if="!isEditMode" class="sm:col-span-2">
            <NvFieldLabel for="plan-owner">负责班组</NvFieldLabel>
            <NvInput
              id="plan-owner"
              v-model="form.owner"
              autocomplete="off"
              placeholder="如 设备保全班"
              :invalid="ownerInvalid"
            />
            <NvFieldError v-if="ownerInvalid" :errors="['请填写负责班组。']" />
          </NvField>
        </NvFieldGroup>

        <NvFieldError v-if="validationMessage" :errors="[validationMessage]" />

        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton type="submit" :disabled="pending">
            <Spinner v-if="pending" aria-hidden="true" />
            {{ isEditMode ? '保存触发条件' : '创建保养计划' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
