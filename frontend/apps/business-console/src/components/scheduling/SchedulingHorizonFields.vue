<script setup lang="ts">
import {
  describeSchedulingHorizon,
  resolveSchedulingHorizon,
  SCHEDULING_HORIZON_PRESET_DAYS,
  type SchedulingHorizonInput,
} from '@/composables/schedulingHorizon'
import {
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    /** 表单控件 id 前缀：同一页可能同时存在多份窗口表单（工作台 + 弹窗）。 */
    idPrefix?: string
    disabled?: boolean
  }>(),
  { idPrefix: 'scheduling-horizon', disabled: false },
)

const model = defineModel<SchedulingHorizonInput>({ required: true })

const resolved = computed(() => resolveSchedulingHorizon(model.value))
const summary = computed(() => describeSchedulingHorizon(resolved.value))

// preset 天数走 NvSelect（值必须是字符串），自定义走两个 datetime-local。
const modeValue = computed({
  get: () => (model.value.mode === 'custom' ? 'custom' : String(model.value.days)),
  set: (value: string) => {
    if (value === 'custom') {
      model.value = { ...model.value, mode: 'custom' }
      return
    }
    const days = Number(value)
    model.value = { ...model.value, mode: 'preset', days: Number.isFinite(days) ? days : 7 }
  },
})

const startLocal = computed({
  get: () => model.value.startLocal,
  set: (value: string) => (model.value = { ...model.value, startLocal: value }),
})
const endLocal = computed({
  get: () => model.value.endLocal,
  set: (value: string) => (model.value = { ...model.value, endLocal: value }),
})
</script>

<template>
  <NvFieldGroup data-testid="scheduling-horizon-fields">
    <NvField>
      <NvFieldLabel :for="`${props.idPrefix}-mode`">排程窗口</NvFieldLabel>
      <NvSelect v-model="modeValue" :disabled="props.disabled">
        <NvSelectTrigger :id="`${props.idPrefix}-mode`" aria-label="排程窗口">
          <NvSelectValue placeholder="选择排程窗口" />
        </NvSelectTrigger>
        <NvSelectContent>
          <NvSelectItem
            v-for="days in SCHEDULING_HORIZON_PRESET_DAYS"
            :key="days"
            :value="`${days}`"
          >
            现在起 {{ days }} 天
          </NvSelectItem>
          <NvSelectItem value="custom">自定义起止时间</NvSelectItem>
        </NvSelectContent>
      </NvSelect>
    </NvField>

    <template v-if="model.mode === 'custom'">
      <NvField>
        <NvFieldLabel :for="`${props.idPrefix}-start`">开始时间</NvFieldLabel>
        <NvInput
          :id="`${props.idPrefix}-start`"
          v-model="startLocal"
          type="datetime-local"
          :disabled="props.disabled"
          :data-invalid="resolved.ok ? undefined : ''"
        />
      </NvField>
      <NvField>
        <NvFieldLabel :for="`${props.idPrefix}-end`">结束时间</NvFieldLabel>
        <NvInput
          :id="`${props.idPrefix}-end`"
          v-model="endLocal"
          type="datetime-local"
          :disabled="props.disabled"
          :data-invalid="resolved.ok ? undefined : ''"
        />
      </NvField>
    </template>

    <p
      class="text-sm"
      :class="resolved.ok ? 'text-muted-foreground' : 'text-destructive'"
      :role="resolved.ok ? 'status' : 'alert'"
      data-testid="scheduling-horizon-summary"
    >
      {{ resolved.ok ? `排程窗口：${summary}` : summary }}
    </p>
  </NvFieldGroup>
</template>
