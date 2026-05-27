<script setup lang="ts">
import { useBusinessContextStore, type BusinessContextState } from '@/stores/businessContext'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@nerv-iip/ui'
import { computed, watch } from 'vue'

export interface BusinessContextOption {
  label: string
  value: string
}

const props = withDefaults(
  defineProps<{
    environmentId: string
    lineCode?: string
    lineOptions?: BusinessContextOption[]
    organizationId: string
    shiftCode?: string
    shiftOptions?: BusinessContextOption[]
    showLine?: boolean
    showShift?: boolean
    showSite?: boolean
    showWorkCenter?: boolean
    siteCode?: string
    siteOptions?: BusinessContextOption[]
    title?: string
    workCenterCode?: string
    workCenterOptions?: BusinessContextOption[]
  }>(),
  {
    lineCode: '',
    lineOptions: () => [],
    shiftCode: '',
    shiftOptions: () => [],
    showLine: true,
    showShift: true,
    showSite: true,
    showWorkCenter: true,
    siteCode: '',
    siteOptions: () => [],
    title: '生产范围',
    workCenterCode: '',
    workCenterOptions: () => [],
  },
)

const emit = defineEmits<{
  change: []
  'update:environmentId': [value: string]
  'update:lineCode': [value: string]
  'update:organizationId': [value: string]
  'update:shiftCode': [value: string]
  'update:siteCode': [value: string]
  'update:workCenterCode': [value: string]
}>()

const context = useBusinessContextStore()

const siteValue = fieldModel('siteCode', 'update:siteCode')
const lineValue = fieldModel('lineCode', 'update:lineCode')
const workCenterValue = fieldModel('workCenterCode', 'update:workCenterCode')
const shiftValue = fieldModel('shiftCode', 'update:shiftCode')

watch(
  () => ({
    environmentId: props.environmentId,
    lineCode: props.lineCode,
    organizationId: props.organizationId,
    shiftCode: props.shiftCode,
    siteCode: props.siteCode,
    workCenterCode: props.workCenterCode,
  }),
  (value) => context.patchContext(value),
  { immediate: true },
)

type UpdateEvent =
  | 'update:environmentId'
  | 'update:lineCode'
  | 'update:organizationId'
  | 'update:shiftCode'
  | 'update:siteCode'
  | 'update:workCenterCode'

function fieldModel(field: keyof BusinessContextState, event: UpdateEvent) {
  return computed({
    get: () => props[field] ?? '',
    set: (value: string) => {
      emitUpdate(event, value)
      context.patchContext({ [field]: value })
      emit('change')
    },
  })
}

function emitUpdate(event: UpdateEvent, value: string) {
  switch (event) {
    case 'update:environmentId':
      emit('update:environmentId', value)
      break
    case 'update:lineCode':
      emit('update:lineCode', value)
      break
    case 'update:organizationId':
      emit('update:organizationId', value)
      break
    case 'update:shiftCode':
      emit('update:shiftCode', value)
      break
    case 'update:siteCode':
      emit('update:siteCode', value)
      break
    case 'update:workCenterCode':
      emit('update:workCenterCode', value)
      break
  }
}

function clearExecutionScope() {
  emit('update:siteCode', '')
  emit('update:lineCode', '')
  emit('update:workCenterCode', '')
  emit('update:shiftCode', '')
  context.clearExecutionScope()
  emit('change')
}
</script>

<template>
  <div class="grid gap-3 rounded-lg border bg-background p-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-sm font-semibold text-foreground">{{ title }}</h2>
      <Button size="sm" type="button" variant="ghost" @click="clearExecutionScope">清空范围</Button>
    </div>
    <FieldGroup class="grid gap-3 md:grid-cols-4 xl:grid-cols-6">
      <Field v-if="showSite">
        <FieldLabel for="business-context-site">工厂</FieldLabel>
        <Select v-if="siteOptions.length" v-model="siteValue">
          <SelectTrigger id="business-context-site">
            <SelectValue placeholder="全部工厂" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in siteOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Input v-else id="business-context-site" v-model="siteValue" placeholder="可选" />
      </Field>
      <Field v-if="showLine">
        <FieldLabel for="business-context-line">产线</FieldLabel>
        <Select v-if="lineOptions.length" v-model="lineValue">
          <SelectTrigger id="business-context-line">
            <SelectValue placeholder="全部产线" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in lineOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Input v-else id="business-context-line" v-model="lineValue" placeholder="可选" />
      </Field>
      <Field v-if="showWorkCenter">
        <FieldLabel for="business-context-work-center">工作中心</FieldLabel>
        <Select v-if="workCenterOptions.length" v-model="workCenterValue">
          <SelectTrigger id="business-context-work-center">
            <SelectValue placeholder="全部工作中心" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Input v-else id="business-context-work-center" v-model="workCenterValue" placeholder="可选" />
      </Field>
      <Field v-if="showShift">
        <FieldLabel for="business-context-shift">班次</FieldLabel>
        <Select v-if="shiftOptions.length" v-model="shiftValue">
          <SelectTrigger id="business-context-shift">
            <SelectValue placeholder="全部班次" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in shiftOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Input v-else id="business-context-shift" v-model="shiftValue" placeholder="可选" />
      </Field>
    </FieldGroup>
    <slot />
  </div>
</template>
