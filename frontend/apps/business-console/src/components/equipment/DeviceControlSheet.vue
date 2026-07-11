<script setup lang="ts">
import type { BusinessConsoleTelemetryTagItem } from '@nerv-iip/api-client'
import {
  deviceControlApprovalLabel,
  deviceControlCommandTypeLabel,
  deviceControlStatusLabel,
  deviceControlStatusTone,
  isTerminalDeviceControlStatus,
  useBusinessDeviceControlCommands,
  type DeviceControlCommandType,
} from '@/composables/useBusinessDeviceControl'
import {
  useBusinessTelemetryHistory,
  useBusinessTelemetryTagCurrentValue,
  useBusinessTelemetryTags,
} from '@/composables/useBusinessTelemetry'
import { notifyError } from '@/utils/notify'
import {
  NvBadge,
  NvButton,
  NvField,
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
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import {
  CheckCircle2Icon,
  Loader2Icon,
  PlusIcon,
  ShieldAlertIcon,
  Trash2Icon,
  XCircleIcon,
} from 'lucide-vue-next'
import { computed, reactive, ref, toRef, watch } from 'vue'

const props = defineProps<{ deviceAssetId: string }>()
const open = defineModel<boolean>('open', { required: true })

const deviceAssetId = toRef(props, 'deviceAssetId')

const { filters: tagFilters, tags } = useBusinessTelemetryTags({
  deviceAssetId: props.deviceAssetId,
  take: 200,
})
const { filters: historyFilters, historyItems } = useBusinessTelemetryHistory({
  deviceAssetId: props.deviceAssetId,
})
const {
  dispatchCommand,
  dispatchPending,
  trackedResult,
  trackedPending,
  trackedError,
  startTracking,
  resetTracking,
} = useBusinessDeviceControlCommands(deviceAssetId)

watch(deviceAssetId, (value) => {
  tagFilters.deviceAssetId = value
  historyFilters.deviceAssetId = value
})

const writableTags = computed(() => tags.value.filter((tag) => tag.isWritable))

type Phase = 'form' | 'tracking'
const phase = ref<Phase>('form')
const commandType = ref<DeviceControlCommandType>('write-tag')
const singleForm = reactive<{ tagKey: string; value: string }>({ tagKey: '', value: '' })
interface ParameterRow {
  tagKey: string
  value: string
}
const parameterRows = reactive<ParameterRow[]>([{ tagKey: '', value: '' }])
const reason = ref('')
const showErrors = ref(false)

// 写值单 tag 的真实当前值（最新原始采样 LastValue，来自专门 current-value 读面），区别于历史合并读面的 bucket 均值。
const singleTagKey = computed(() => singleForm.tagKey)
const { currentValue: singleCurrentValue } = useBusinessTelemetryTagCurrentValue(
  deviceAssetId,
  singleTagKey,
)

function resetForm() {
  phase.value = 'form'
  commandType.value = 'write-tag'
  singleForm.tagKey = ''
  singleForm.value = ''
  parameterRows.splice(0, parameterRows.length, { tagKey: '', value: '' })
  reason.value = ''
  showErrors.value = false
  resetTracking()
}

watch(open, (isOpen) => {
  if (isOpen) resetForm()
  else resetTracking()
})

const isSingleTag = computed(
  () => commandType.value === 'write-tag' || commandType.value === 'start-stop',
)

function tagByKey(tagKey: string): BusinessConsoleTelemetryTagItem | undefined {
  return writableTags.value.find((tag) => tag.tagKey === tagKey)
}

// 只取最新一条原始采样（itemType==='sample'）。历史读面同时合并 raw 采样与 hourly/daily rollup，
// 且值均为 bucket 平均值——没有专门的瞬时当前值 facade，故如实标注为「最近采样(均值)」，不冒充实时当前值。
function latestSampleValue(tagKey: string): string | null {
  if (!tagKey) return null
  const matches = historyItems.value
    .filter((item) => item.tagKey === tagKey && item.itemType === 'sample' && item.value)
    .sort(
      (a, b) => new Date(b.occurredAtUtc ?? 0).getTime() - new Date(a.occurredAtUtc ?? 0).getTime(),
    )
  return matches[0]?.value ?? null
}

function rangeHint(tag?: BusinessConsoleTelemetryTagItem): string {
  if (!tag) return ''
  const allowed = tag.controlAllowedValues ?? []
  if (allowed.length) return `允许值：${allowed.join(' / ')}`
  const min = tag.controlMinValue
  const max = tag.controlMaxValue
  const unit = tag.unitCode ? ` ${tag.unitCode}` : ''
  if (min != null && max != null) return `值域：${min} ~ ${max}${unit}`
  if (min != null) return `不小于 ${min}${unit}`
  if (max != null) return `不大于 ${max}${unit}`
  return tag.unitCode ? `单位：${tag.unitCode}` : ''
}

// 前端即时校验：类型 / 越界 / 允许值；后端 ValidateWritableTag 仍为权威兜底。
function validateValue(tagKey: string, value: string): string | null {
  const tag = tagByKey(tagKey)
  if (!tagKey) return '请选择采集点'
  if (!value.trim()) return '请填写下发值'
  if (!tag) return null
  const allowed = tag.controlAllowedValues ?? []
  if (allowed.length && !allowed.some((a) => a.toLowerCase() === value.trim().toLowerCase())) {
    return `值必须是：${allowed.join(' / ')}`
  }
  if (tag.valueType === 'number') {
    const numeric = Number(value)
    if (Number.isNaN(numeric)) return '值必须为数字'
    if (tag.controlMinValue != null && numeric < tag.controlMinValue)
      return `低于允许最小值 ${tag.controlMinValue}`
    if (tag.controlMaxValue != null && numeric > tag.controlMaxValue)
      return `高于允许最大值 ${tag.controlMaxValue}`
  }
  return null
}

const singleValueError = computed(() => validateValue(singleForm.tagKey, singleForm.value))
const parameterRowErrors = computed(() =>
  parameterRows.map((row) => validateValue(row.tagKey, row.value)),
)
const reasonError = computed(() => (reason.value.trim() ? null : '请填写下发原因'))

const formValid = computed(() => {
  if (reasonError.value) return false
  if (isSingleTag.value) return !singleValueError.value
  const filled = parameterRows.filter((row) => row.tagKey || row.value)
  if (!filled.length) return false
  return parameterRowErrors.value.every((error) => !error)
})

function addParameterRow() {
  parameterRows.push({ tagKey: '', value: '' })
}
function removeParameterRow(index: number) {
  parameterRows.splice(index, 1)
  if (!parameterRows.length) parameterRows.push({ tagKey: '', value: '' })
}

async function submit() {
  showErrors.value = true
  if (!formValid.value) return
  try {
    let commandId: string | undefined
    if (isSingleTag.value) {
      commandId = await dispatchCommand({
        commandType: commandType.value,
        tagKey: singleForm.tagKey,
        value: singleForm.value.trim(),
        reason: reason.value.trim(),
      })
    } else {
      const parameters: Record<string, string> = {}
      for (const row of parameterRows) {
        if (row.tagKey && row.value) parameters[row.tagKey] = row.value.trim()
      }
      commandId = await dispatchCommand({
        commandType: 'parameter-set',
        parameters,
        reason: reason.value.trim(),
      })
    }
    if (commandId) {
      startTracking(commandId)
      phase.value = 'tracking'
    }
  } catch (error) {
    notifyError(error, '设备控制命令下发失败，请稍后重试。')
  }
}

const trackedStatus = computed(() => trackedResult.value?.status)
const trackedTerminal = computed(() => isTerminalDeviceControlStatus(trackedStatus.value))
const trackedFailedStatus = computed(() => trackedStatus.value?.toLowerCase() === 'failed')

// 优先展示 Connector 回执里的设备实际回执码（attempt.output.deviceReceiptCode，如 BadOutOfRange），
// 其次回退 Ops 通用 failureCode（如 opcua.write.rejected）。
const trackedReceipt = computed(() => {
  const attempts = trackedResult.value?.attempts ?? []
  const failed = [...attempts]
    .reverse()
    .find((attempt) => attempt.output?.deviceReceiptCode || attempt.failureCode)
  if (!failed) return null
  const deviceCode = failed.output?.deviceReceiptCode ?? null
  const connectorCode = failed.failureCode ?? null
  return {
    code: deviceCode ?? connectorCode,
    message: failed.output?.deviceReceiptMessage ?? null,
    // 设备回执码与连接器通用码不同才另行展示连接器码，避免重复。
    connectorCode:
      deviceCode && connectorCode && deviceCode !== connectorCode ? connectorCode : null,
  }
})
// 失败但没有 attempt 明细（Ops 不可用回退台账快照）时，给出明确反馈而非空白。
const trackedFailedWithoutReceipt = computed(
  () => trackedFailedStatus.value && !trackedReceipt.value,
)

const noWritableTags = computed(() => writableTags.value.length === 0)
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-xl">
      <NvSheetHeader class="border-b">
        <NvSheetTitle>设备控制 · {{ deviceAssetId }}</NvSheetTitle>
        <NvSheetDescription>
          命令通过设备绑定的连接器通道下发，需 Ops 审批门禁·全程审计。
        </NvSheetDescription>
      </NvSheetHeader>

      <!-- 表单阶段 -->
      <div v-if="phase === 'form'" class="grid gap-4 p-4">
        <NvTabs v-model="commandType">
          <NvTabsList class="w-full">
            <NvTabsTrigger value="write-tag" class="flex-1">写值</NvTabsTrigger>
            <NvTabsTrigger value="start-stop" class="flex-1">启停</NvTabsTrigger>
            <NvTabsTrigger value="parameter-set" class="flex-1">参数下发</NvTabsTrigger>
          </NvTabsList>
        </NvTabs>

        <div
          v-if="noWritableTags"
          class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
        >
          该设备没有可写采集点，无法下发控制命令。请先在「采集标签」为该设备配置可写值域。
        </div>

        <!-- 写值 / 启停：单采集点 -->
        <template v-else-if="isSingleTag">
          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="devctl-tag">采集点</NvFieldLabel>
              <NvSelect v-model="singleForm.tagKey">
                <NvSelectTrigger id="devctl-tag" aria-label="采集点">
                  <NvSelectValue placeholder="选择可写采集点" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="tag in writableTags"
                    :key="tag.telemetryTagId"
                    :value="tag.tagKey ?? ''"
                  >
                    {{ tag.tagKey }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>

            <div
              v-if="singleForm.tagKey"
              class="grid grid-cols-2 gap-2 rounded-md bg-muted p-3 text-xs"
            >
              <div>
                <p class="text-muted-foreground">值域范围</p>
                <p class="font-medium text-foreground">
                  {{ rangeHint(tagByKey(singleForm.tagKey)) || '未标注' }}
                </p>
              </div>
              <div>
                <p class="text-muted-foreground">当前值</p>
                <p class="font-medium text-foreground">
                  {{ singleCurrentValue?.hasSample ? singleCurrentValue.value : '无采样' }}
                </p>
              </div>
            </div>

            <NvField>
              <NvFieldLabel for="devctl-value"
                >下发值 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect
                v-if="(tagByKey(singleForm.tagKey)?.controlAllowedValues?.length ?? 0) > 0"
                v-model="singleForm.value"
              >
                <NvSelectTrigger id="devctl-value" aria-label="下发值">
                  <NvSelectValue placeholder="选择下发值" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="allowed in tagByKey(singleForm.tagKey)?.controlAllowedValues ?? []"
                    :key="allowed"
                    :value="allowed"
                  >
                    {{ allowed }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <NvInput
                v-else
                id="devctl-value"
                v-model="singleForm.value"
                placeholder="填写下发值"
                :aria-invalid="showErrors && !!singleValueError"
              />
              <p
                v-if="showErrors && singleValueError"
                class="text-xs text-destructive"
                role="alert"
              >
                {{ singleValueError }}
              </p>
            </NvField>
          </NvFieldGroup>
        </template>

        <!-- 参数下发：动态行 -->
        <template v-else>
          <div class="grid gap-2">
            <span class="text-sm font-medium text-foreground">参数集</span>
            <div
              v-for="(row, index) in parameterRows"
              :key="index"
              class="grid gap-2 rounded-md border p-3"
            >
              <div class="flex items-start gap-2">
                <div class="grid flex-1 gap-2">
                  <NvSelect v-model="row.tagKey">
                    <NvSelectTrigger :aria-label="`参数采集点 ${index + 1}`">
                      <NvSelectValue placeholder="选择采集点" />
                    </NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="tag in writableTags"
                        :key="tag.telemetryTagId"
                        :value="tag.tagKey ?? ''"
                      >
                        {{ tag.tagKey }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                  <NvInput
                    v-model="row.value"
                    placeholder="下发值"
                    :aria-label="`参数值 ${index + 1}`"
                  />
                  <p v-if="row.tagKey" class="text-xs text-muted-foreground">
                    {{ rangeHint(tagByKey(row.tagKey)) }}
                    <span v-if="latestSampleValue(row.tagKey)">
                      · 最近采样(均值) {{ latestSampleValue(row.tagKey) }}</span
                    >
                  </p>
                  <p
                    v-if="showErrors && parameterRowErrors[index]"
                    class="text-xs text-destructive"
                    role="alert"
                  >
                    {{ parameterRowErrors[index] }}
                  </p>
                </div>
                <NvButton
                  size="sm"
                  type="button"
                  variant="ghost"
                  aria-label="删除参数行"
                  @click="removeParameterRow(index)"
                >
                  <Trash2Icon aria-hidden="true" />
                </NvButton>
              </div>
            </div>
            <NvButton
              size="sm"
              type="button"
              variant="outline"
              class="justify-self-start"
              @click="addParameterRow"
            >
              <PlusIcon aria-hidden="true" />
              添加参数
            </NvButton>
          </div>
        </template>

        <NvField v-if="!noWritableTags">
          <NvFieldLabel for="devctl-reason"
            >下发原因 <span class="text-destructive">*</span></NvFieldLabel
          >
          <textarea
            id="devctl-reason"
            v-model="reason"
            rows="2"
            class="min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            placeholder="说明下发目的，进审计"
          ></textarea>
          <p v-if="showErrors && reasonError" class="text-xs text-destructive" role="alert">
            {{ reasonError }}
          </p>
        </NvField>

        <div
          v-if="!noWritableTags"
          class="flex items-start gap-2 rounded-md border border-warning/40 bg-warning/10 p-3 text-sm"
        >
          <ShieldAlertIcon class="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />
          <p class="text-foreground">
            该命令需经 <span class="font-semibold">Ops 审批门禁</span>，通过后才会下发到设备，<span
              class="font-semibold"
              >全程审计</span
            >。
          </p>
        </div>
      </div>

      <!-- 状态跟踪阶段 -->
      <div v-else class="grid gap-4 p-4">
        <div class="rounded-lg border bg-card p-4">
          <div class="flex items-center justify-between gap-3">
            <div class="min-w-0">
              <p class="text-sm text-muted-foreground">命令状态</p>
              <p class="mt-1 flex items-center gap-2 text-lg font-semibold text-foreground">
                <Loader2Icon
                  v-if="!trackedTerminal"
                  class="size-4 animate-spin text-warning"
                  aria-hidden="true"
                />
                <CheckCircle2Icon
                  v-else-if="trackedStatus?.toLowerCase() === 'completed'"
                  class="size-4 text-success"
                  aria-hidden="true"
                />
                <XCircleIcon v-else class="size-4 text-destructive" aria-hidden="true" />
                {{ deviceControlStatusLabel(trackedStatus) }}
              </p>
            </div>
            <NvBadge class="rounded-sm" :variant="deviceControlStatusTone(trackedStatus)">
              {{ deviceControlCommandTypeLabel(trackedResult?.commandType) }}
            </NvBadge>
          </div>
          <p v-if="trackedPending && !trackedResult" class="mt-3 text-sm text-muted-foreground">
            正在读取命令结果…
          </p>
          <p
            v-else-if="trackedError && !trackedResult"
            class="mt-3 text-sm text-destructive"
            role="alert"
          >
            读取命令结果失败，正在自动重试；也可稍后在「控制命令历史」查看。
          </p>
        </div>

        <div class="grid gap-2 text-sm">
          <div class="grid grid-cols-[96px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">审批状态</span>
            <span class="font-medium text-foreground">{{
              deviceControlApprovalLabel(trackedResult?.approval?.status)
            }}</span>
          </div>
          <div
            v-if="trackedResult?.approval?.decisionReason"
            class="grid grid-cols-[96px_minmax(0,1fr)] gap-2"
          >
            <span class="text-muted-foreground">审批意见</span>
            <span class="text-foreground">{{ trackedResult.approval.decisionReason }}</span>
          </div>
          <div v-if="trackedReceipt" class="grid grid-cols-[96px_minmax(0,1fr)] gap-2">
            <span class="text-muted-foreground">设备回执码</span>
            <span class="grid gap-0.5">
              <span class="font-mono text-destructive">{{ trackedReceipt.code }}</span>
              <span v-if="trackedReceipt.message" class="text-xs text-muted-foreground">{{
                trackedReceipt.message
              }}</span>
              <span v-if="trackedReceipt.connectorCode" class="text-xs text-muted-foreground"
                >连接器码 {{ trackedReceipt.connectorCode }}</span
              >
            </span>
          </div>
          <div
            v-else-if="trackedFailedWithoutReceipt"
            class="grid grid-cols-[96px_minmax(0,1fr)] gap-2"
          >
            <span class="text-muted-foreground">设备回执码</span>
            <span class="text-xs text-muted-foreground"
              >命令失败，但暂未获取到设备回执明细（Ops 回执尚未回传）。</span
            >
          </div>
        </div>

        <div v-if="!trackedTerminal" class="rounded-md bg-muted p-3 text-xs text-muted-foreground">
          命令已提交，正在等待审批与执行，本面板会自动刷新状态。可关闭抽屉，稍后在「控制命令历史」查看结果。
        </div>
      </div>

      <NvSheetFooter class="mt-auto border-t p-4">
        <template v-if="phase === 'form'">
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="button" :disabled="noWritableTags || dispatchPending" @click="submit">
            提交下发
          </NvButton>
        </template>
        <template v-else>
          <NvButton type="button" variant="outline" @click="resetForm">再下发一条</NvButton>
          <NvButton type="button" @click="open = false">关闭</NvButton>
        </template>
      </NvSheetFooter>
    </NvSheetContent>
  </NvSheet>
</template>
