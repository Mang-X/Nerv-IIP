<script setup lang="ts">
import type { BusinessConsoleTelemetryTagItem } from '@nerv-iip/api-client'
import {
  formatSamplingPolicy,
  formatTelemetryUnit,
  formatTelemetryValueType,
  isProductionCountValueType,
  normalizeTelemetryValueType,
  TELEMETRY_SAMPLING_POLICY_OPTIONS,
  TELEMETRY_UNIT_LABELS,
  TELEMETRY_VALUE_TYPE_OPTIONS,
} from '@/data/businessLabels'
import { useBusinessTelemetryPoints } from '@/composables/useBusinessTelemetryPoints'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvBadge,
  NvButton,
  NvCheckbox,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { PencilIcon, PlusIcon, PowerOffIcon } from '@lucide/vue'
import { computed, reactive, ref, toRef, watch } from 'vue'

/**
 * 设备采集点位维护抽屉（#3870）。在设备详情里就地新建、编辑、停用这台设备的采集点位，
 * 不跳页。点位编码是与连接器配置对接的键，由用户维护、保存后锁定；点位只停用不删除。
 */
const props = defineProps<{
  deviceAssetId: string
  deviceTitle: string
  canManage: boolean
}>()
const open = defineModel<boolean>('open', { required: true })

const {
  points,
  pointsError,
  pointsPending,
  savePoint,
  savePointPending,
  disablePoint,
  disablePointPending,
} = useBusinessTelemetryPoints(toRef(props, 'deviceAssetId'))

type Phase = 'list' | 'form' | 'disable'
const phase = ref<Phase>('list')
const editing = ref<BusinessConsoleTelemetryTagItem | null>(null)
const disableTarget = ref<BusinessConsoleTelemetryTagItem | null>(null)
const showErrors = ref(false)

const TAG_KEY_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]*$/

const form = reactive({
  tagKey: '',
  displayName: '',
  valueType: '',
  unitCode: '',
  samplingPolicy: 'sample-60s',
  isWritable: false,
  controlMinValue: '',
  controlMaxValue: '',
  controlAllowedValues: [] as string[],
})

watch(open, (value) => {
  if (value) backToList()
})

const listErrorMessage = computed(() => inlineErrorMessage(pointsError.value))

const unitOptions = computed(() => {
  const options = Object.entries(TELEMETRY_UNIT_LABELS)
    .filter(([code]) => code !== '%')
    .map(([code, name]) => ({ value: code, label: `${name}（${code}）` }))
  const current = form.unitCode.trim()
  if (current && !options.some((option) => option.value === current)) {
    options.unshift({ value: current, label: formatTelemetryUnit(current) })
  }
  return options
})

const samplingOptions = computed(() => {
  const options: { value: string; label: string }[] = TELEMETRY_SAMPLING_POLICY_OPTIONS.map(
    (option) => ({ ...option }),
  )
  const current = form.samplingPolicy.trim()
  if (current && !options.some((option) => option.value === current)) {
    options.unshift({ value: current, label: formatSamplingPolicy(current) })
  }
  return options
})

const isCounting = computed(() => isProductionCountValueType(form.valueType))
const isNumeric = computed(() => form.valueType === 'number')
// 计数点位是设备往上报的产量，不是往下写的设定值：不开放远程写入。
const canBeWritable = computed(() => !!form.valueType && !isCounting.value)

watch(canBeWritable, (value) => {
  if (!value) form.isWritable = false
})

const tagKeyError = computed(() => {
  if (!showErrors.value || editing.value) return ''
  const tagKey = form.tagKey.trim()
  if (!tagKey) return '请填写点位编码'
  if (tagKey.length > 150) return '点位编码不能超过 150 个字符'
  if (!TAG_KEY_PATTERN.test(tagKey)) return '点位编码只能用字母、数字、点、下划线和短横线'
  const existing = points.value.find(
    (point) => (point.tagKey ?? '').toLowerCase() === tagKey.toLowerCase(),
  )
  if (existing?.isEnabled === false) return '这个编码的点位已停用，不能再用，请换一个编码'
  if (existing) return '这台设备已有同编码的点位'
  return ''
})
const displayNameError = computed(() =>
  showErrors.value && form.displayName.trim().length > 100 ? '点位名称不能超过 100 个字符' : '',
)
const valueTypeError = computed(() => (showErrors.value && !form.valueType ? '请选择数据类型' : ''))
const unitError = computed(() => (showErrors.value && !form.unitCode.trim() ? '请选择单位' : ''))
const samplingError = computed(() =>
  showErrors.value && !form.samplingPolicy.trim() ? '请选择采集周期' : '',
)

function parseOptionalNumber(value: string): number | null | 'invalid' {
  const trimmed = String(value ?? '').trim()
  if (!trimmed) return null
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : 'invalid'
}

const rangeError = computed(() => {
  if (!showErrors.value || !form.isWritable || !isNumeric.value) return ''
  const min = parseOptionalNumber(form.controlMinValue)
  const max = parseOptionalNumber(form.controlMaxValue)
  if (min === 'invalid' || max === 'invalid') return '写入上下限必须是数字'
  if (min !== null && max !== null && min > max) return '写入下限不能大于上限'
  return ''
})

const formValid = computed(
  () =>
    !tagKeyError.value &&
    !displayNameError.value &&
    !valueTypeError.value &&
    !unitError.value &&
    !samplingError.value &&
    !rangeError.value,
)

function pointTitle(point: BusinessConsoleTelemetryTagItem) {
  return point.displayName?.trim() || point.tagKey || '—'
}

function pointFacts(point: BusinessConsoleTelemetryTagItem) {
  return [
    `编码 ${point.tagKey ?? '—'}`,
    formatTelemetryValueType(point.valueType),
    formatTelemetryUnit(point.unitCode, '—'),
    formatSamplingPolicy(point.samplingPolicy, '—'),
  ].join(' · ')
}

function backToList() {
  phase.value = 'list'
  editing.value = null
  disableTarget.value = null
  showErrors.value = false
}

function openCreate() {
  editing.value = null
  Object.assign(form, {
    tagKey: '',
    displayName: '',
    valueType: '',
    unitCode: '',
    samplingPolicy: 'sample-60s',
    isWritable: false,
    controlMinValue: '',
    controlMaxValue: '',
    controlAllowedValues: [],
  })
  showErrors.value = false
  phase.value = 'form'
}

function openEdit(point: BusinessConsoleTelemetryTagItem) {
  editing.value = point
  Object.assign(form, {
    tagKey: point.tagKey ?? '',
    displayName: point.displayName ?? '',
    valueType: normalizeTelemetryValueType(point.valueType),
    unitCode: point.unitCode ?? '',
    samplingPolicy: point.samplingPolicy ?? '',
    isWritable: point.isWritable === true,
    controlMinValue: point.controlMinValue == null ? '' : String(point.controlMinValue),
    controlMaxValue: point.controlMaxValue == null ? '' : String(point.controlMaxValue),
    // 允许值不在这张表单里编辑，但必须原样回传，否则 upsert 会把它清空。
    controlAllowedValues: [...(point.controlAllowedValues ?? [])],
  })
  showErrors.value = false
  phase.value = 'form'
}

function openDisable(point: BusinessConsoleTelemetryTagItem) {
  disableTarget.value = point
  phase.value = 'disable'
}

async function submit() {
  showErrors.value = true
  if (!formValid.value) return
  const writable = form.isWritable && canBeWritable.value
  const min = writable && isNumeric.value ? parseOptionalNumber(form.controlMinValue) : null
  const max = writable && isNumeric.value ? parseOptionalNumber(form.controlMaxValue) : null
  const tagKey = form.tagKey.trim()
  const title = form.displayName.trim() || tagKey
  try {
    await savePoint({
      tagKey,
      displayName: form.displayName.trim() || null,
      valueType: form.valueType,
      unitCode: form.unitCode.trim(),
      samplingPolicy: form.samplingPolicy.trim(),
      isWritable: writable,
      controlMinValue: typeof min === 'number' ? min : null,
      controlMaxValue: typeof max === 'number' ? max : null,
      controlAllowedValues: writable ? form.controlAllowedValues : [],
    })
    notifySuccess(editing.value ? `采集点位已更新：${title}` : `采集点位已新建：${title}`)
    backToList()
  } catch (error) {
    notifyOperationFailure('保存采集点位失败', error, '保存采集点位失败，请稍后重试。')
  }
}

async function confirmDisable() {
  const target = disableTarget.value
  if (!target?.tagKey) return
  try {
    await disablePoint(target.tagKey)
    notifySuccess(`采集点位已停用：${pointTitle(target)}`)
    backToList()
  } catch (error) {
    notifyOperationFailure('停用采集点位失败', error, '停用采集点位失败，请稍后重试。')
  }
}
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-xl">
      <NvSheetHeader class="border-b">
        <NvSheetTitle>采集点位 · {{ deviceTitle }}</NvSheetTitle>
        <NvSheetDescription>
          点位编码和采集周期要与连接器里的配置一致，否则采集不到或采样会被拒收。
        </NvSheetDescription>
      </NvSheetHeader>

      <!-- 列表 -->
      <div v-if="phase === 'list'" class="grid gap-3 p-4">
        <div class="flex items-center justify-between gap-3">
          <p class="text-sm text-muted-foreground">
            {{ points.length ? `共 ${points.length} 个点位` : '' }}
          </p>
          <NvButton v-if="canManage" size="sm" type="button" @click="openCreate">
            <PlusIcon aria-hidden="true" />
            新建点位
          </NvButton>
        </div>

        <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
          {{ listErrorMessage }}
        </p>
        <p v-else-if="pointsPending && !points.length" class="text-sm text-muted-foreground">
          正在读取采集点位…
        </p>
        <div
          v-else-if="!points.length"
          class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
        >
          这台设备还没有采集点位。{{ canManage ? '点「新建点位」添加。' : '' }}
        </div>

        <ul v-else class="grid gap-2" aria-label="采集点位">
          <li
            v-for="point in points"
            :key="point.telemetryTagId ?? point.tagKey ?? ''"
            class="grid gap-1 rounded-lg border p-3"
            :class="point.isEnabled === false ? 'bg-muted/40' : ''"
          >
            <div class="flex flex-wrap items-center justify-between gap-2">
              <div class="flex min-w-0 flex-wrap items-center gap-2">
                <span class="truncate font-medium text-foreground">{{ pointTitle(point) }}</span>
                <NvStatusBadge :value="point.isEnabled === false ? 'disabled' : 'active'" />
                <NvBadge v-if="point.isWritable" variant="neutral">可远程写入</NvBadge>
              </div>
              <div
                v-if="canManage && point.isEnabled !== false"
                class="flex shrink-0 items-center gap-1"
              >
                <NvButton size="sm" type="button" variant="ghost" @click="openEdit(point)">
                  <PencilIcon aria-hidden="true" />
                  编辑
                </NvButton>
                <NvButton size="sm" type="button" variant="ghost" @click="openDisable(point)">
                  <PowerOffIcon aria-hidden="true" />
                  停用
                </NvButton>
              </div>
            </div>
            <p class="text-xs break-all text-muted-foreground">{{ pointFacts(point) }}</p>
          </li>
        </ul>
      </div>

      <!-- 新建 / 编辑 -->
      <form v-else-if="phase === 'form'" class="grid gap-4 p-4" @submit.prevent="submit">
        <h3 class="text-sm font-semibold text-foreground">
          {{ editing ? `编辑点位：${pointTitle(editing)}` : '新建点位' }}
        </h3>
        <NvFieldGroup class="grid gap-3">
          <NvField>
            <NvFieldLabel for="point-name">点位名称</NvFieldLabel>
            <NvInput
              id="point-name"
              v-model="form.displayName"
              placeholder="如：成品计数、主轴转速"
              :invalid="!!displayNameError"
            />
            <p v-if="displayNameError" class="text-xs text-destructive" role="alert">
              {{ displayNameError }}
            </p>
          </NvField>

          <NvField>
            <NvFieldLabel for="point-key">
              点位编码 <span v-if="!editing" class="text-destructive">*</span>
            </NvFieldLabel>
            <p v-if="editing" id="point-key" class="text-sm text-foreground">
              {{ form.tagKey }}
            </p>
            <template v-else>
              <NvInput
                id="point-key"
                v-model="form.tagKey"
                placeholder="与连接器配置中的点位编码一致，如 parts_count"
                :invalid="!!tagKeyError"
              />
              <NvFieldDescription>保存后不能修改。</NvFieldDescription>
            </template>
            <p v-if="tagKeyError" class="text-xs text-destructive" role="alert">
              {{ tagKeyError }}
            </p>
          </NvField>

          <NvField>
            <NvFieldLabel for="point-value-type">
              数据类型 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSelect v-model="form.valueType">
              <NvSelectTrigger id="point-value-type" aria-label="数据类型">
                <NvSelectValue placeholder="选择数据类型" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in TELEMETRY_VALUE_TYPE_OPTIONS"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <NvFieldDescription v-if="isCounting">
              计数点位按两次采样的差值报工；「待人工确认」会在 PDA 上生成待确认的遥测记录。
            </NvFieldDescription>
            <p v-if="valueTypeError" class="text-xs text-destructive" role="alert">
              {{ valueTypeError }}
            </p>
          </NvField>

          <div class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="point-unit">
                单位 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvSelect v-model="form.unitCode">
                <NvSelectTrigger id="point-unit" aria-label="单位">
                  <NvSelectValue placeholder="选择单位" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in unitOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="unitError" class="text-xs text-destructive" role="alert">
                {{ unitError }}
              </p>
            </NvField>
            <NvField>
              <NvFieldLabel for="point-sampling">
                采集周期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvSelect v-model="form.samplingPolicy">
                <NvSelectTrigger id="point-sampling" aria-label="采集周期">
                  <NvSelectValue placeholder="选择采集周期" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in samplingOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <p v-if="samplingError" class="text-xs text-destructive" role="alert">
                {{ samplingError }}
              </p>
            </NvField>
          </div>

          <NvField
            v-if="canBeWritable"
            orientation="horizontal"
            class="items-center justify-between gap-3 rounded-lg border px-3 py-2"
          >
            <NvFieldLabel for="point-writable" class="mb-0">允许远程写入</NvFieldLabel>
            <NvCheckbox id="point-writable" v-model="form.isWritable" />
          </NvField>

          <div v-if="form.isWritable && isNumeric" class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="point-min">写入下限</NvFieldLabel>
              <NvInput
                id="point-min"
                v-model="form.controlMinValue"
                inputmode="decimal"
                placeholder="不限"
                :invalid="!!rangeError"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="point-max">写入上限</NvFieldLabel>
              <NvInput
                id="point-max"
                v-model="form.controlMaxValue"
                inputmode="decimal"
                placeholder="不限"
                :invalid="!!rangeError"
              />
            </NvField>
            <p v-if="rangeError" class="text-xs text-destructive sm:col-span-2" role="alert">
              {{ rangeError }}
            </p>
          </div>
        </NvFieldGroup>

        <NvSheetFooter class="flex-row justify-end gap-2 p-0">
          <NvButton type="button" variant="outline" @click="backToList">取消</NvButton>
          <NvButton type="submit" :disabled="savePointPending">
            {{ editing ? '保存点位' : '新建点位' }}
          </NvButton>
        </NvSheetFooter>
      </form>

      <!-- 停用确认 -->
      <div v-else-if="phase === 'disable' && disableTarget" class="grid gap-4 p-4">
        <h3 class="text-sm font-semibold text-foreground">
          停用点位：{{ pointTitle(disableTarget) }}
        </h3>
        <p class="text-sm text-muted-foreground">
          停用后，这个点位不再计数，也不会出现在报警规则、历史趋势和设备控制的点位选择里；已采集的历史数据保留。停用不能撤回，同一个编码也不能再用。
        </p>
        <NvSheetFooter class="flex-row justify-end gap-2 p-0">
          <NvButton type="button" variant="outline" @click="backToList">取消</NvButton>
          <NvButton
            type="button"
            variant="destructive"
            :disabled="disablePointPending"
            @click="confirmDisable"
          >
            确认停用
          </NvButton>
        </NvSheetFooter>
      </div>
    </NvSheetContent>
  </NvSheet>
</template>
