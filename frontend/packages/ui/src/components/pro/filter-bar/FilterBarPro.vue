<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { SearchIcon, XIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import BadgePro from '../badge/BadgePro.vue'
import ButtonPro from '../button/ButtonPro.vue'
import DatePickerPro from '../date-picker/DatePickerPro.vue'
import InputPro from '../input/InputPro.vue'
import {
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
} from '../select'

export interface FilterFieldOption {
  label: string
  value: string
}

export interface FilterField {
  key: string
  label: string
  type: 'select' | 'date' | 'daterange'
  /** Required for `select`; ignored otherwise. */
  options?: FilterFieldOption[]
  placeholder?: string
}

/**
 * Pro — data-driven filter bar for list/workbench pages. A keyword search,
 * any number of field filters (select / date / date-range), removable active
 * chips, and a reset action that only appears once something is filtered.
 * Composes InputPro / SelectPro / DatePickerPro / BadgePro / ButtonPro — no
 * 原版 components touched.
 */
const props = withDefaults(
  defineProps<{
    fields?: FilterField[]
    /** Per-field values. `daterange` stores `{ from?, to? }`. */
    modelValue?: Record<string, any>
    keyword?: string
    searchPlaceholder?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    fields: () => [],
    modelValue: () => ({}),
    keyword: '',
    searchPlaceholder: '搜索关键字',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, any>]
  'update:keyword': [value: string]
  reset: []
  change: [payload: { values: Record<string, any>; keyword: string }]
}>()

function isEmptyValue(v: any) {
  if (v == null || v === '') return true
  if (typeof v === 'object') return !v.from && !v.to
  return false
}

const fieldMap = computed(() =>
  Object.fromEntries(props.fields.map((f) => [f.key, f])),
)

function setField(key: string, value: any) {
  const next = { ...props.modelValue, [key]: value }
  emit('update:modelValue', next)
  emit('change', { values: next, keyword: props.keyword })
}

function setRangePart(key: string, part: 'from' | 'to', value: string) {
  const current = (props.modelValue[key] ?? {}) as { from?: string; to?: string }
  setField(key, { ...current, [part]: value })
}

function setKeyword(value: string | number) {
  const next = String(value ?? '')
  emit('update:keyword', next)
  emit('change', { values: props.modelValue, keyword: next })
}

function optionLabel(field: FilterField, value: string) {
  return field.options?.find((o) => o.value === value)?.label ?? value
}

function formatChip(field: FilterField, value: any): string {
  if (field.type === 'select') return optionLabel(field, value)
  if (field.type === 'date') return value
  const from = value?.from ?? '…'
  const to = value?.to ?? '…'
  return `${from} ~ ${to}`
}

const activeChips = computed(() =>
  props.fields
    .filter((f) => !isEmptyValue(props.modelValue[f.key]))
    .map((f) => ({
      key: f.key,
      label: f.label,
      text: formatChip(f, props.modelValue[f.key]),
    })),
)

const hasActive = computed(
  () => activeChips.value.length > 0 || !isEmptyValue(props.keyword),
)

function clearField(key: string) {
  setField(key, fieldMap.value[key]?.type === 'daterange' ? {} : undefined)
}

function reset() {
  emit('update:modelValue', {})
  emit('update:keyword', '')
  emit('reset')
  emit('change', { values: {}, keyword: '' })
}
</script>

<template>
  <div data-slot="filter-bar-pro" :class="cn('flex flex-col gap-3', props.class)">
    <div class="flex flex-wrap items-center gap-2">
      <InputPro
        :model-value="keyword"
        :placeholder="searchPlaceholder"
        class="w-full sm:w-64"
        type="search"
        :aria-label="searchPlaceholder"
        @update:model-value="setKeyword"
      >
        <template #leading>
          <SearchIcon aria-hidden="true" />
        </template>
      </InputPro>

      <template v-for="field in fields" :key="field.key">
        <SelectPro
          v-if="field.type === 'select'"
          :model-value="modelValue[field.key] ?? ''"
          @update:model-value="(v) => setField(field.key, v || undefined)"
        >
          <SelectProTrigger
            :aria-label="field.label"
            class="w-full sm:w-44"
          >
            <SelectProValue :placeholder="field.placeholder ?? field.label" />
          </SelectProTrigger>
          <SelectProContent>
            <SelectProItem
              v-for="opt in field.options ?? []"
              :key="opt.value"
              :value="opt.value"
            >
              {{ opt.label }}
            </SelectProItem>
          </SelectProContent>
        </SelectPro>

        <DatePickerPro
          v-else-if="field.type === 'date'"
          :model-value="modelValue[field.key] ?? null"
          :placeholder="field.placeholder ?? field.label"
          @update:model-value="(v) => setField(field.key, v || undefined)"
        />

        <div v-else class="flex items-center gap-1.5">
          <DatePickerPro
            :model-value="modelValue[field.key]?.from ?? null"
            :placeholder="`${field.label} · 起`"
            class="w-40"
            @update:model-value="(v) => setRangePart(field.key, 'from', v)"
          />
          <span class="text-sm text-muted-foreground" aria-hidden="true">~</span>
          <DatePickerPro
            :model-value="modelValue[field.key]?.to ?? null"
            :placeholder="`${field.label} · 止`"
            class="w-40"
            @update:model-value="(v) => setRangePart(field.key, 'to', v)"
          />
        </div>
      </template>

      <ButtonPro
        v-if="hasActive"
        variant="ghost"
        size="sm"
        class="ms-auto text-muted-foreground"
        @click="reset"
      >
        <template #leading>
          <XIcon aria-hidden="true" />
        </template>
        重置
      </ButtonPro>
    </div>

    <div v-if="activeChips.length" class="flex flex-wrap items-center gap-1.5">
      <BadgePro
        v-for="chip in activeChips"
        :key="chip.key"
        variant="brand"
        class="gap-1 pr-1"
      >
        <span class="text-muted-foreground">{{ chip.label }}:</span>
        <span class="tabular-nums">{{ chip.text }}</span>
        <button
          type="button"
          class="grid size-3.5 place-items-center rounded-full text-brand-strong/70 transition-colors hover:bg-brand/20 hover:text-brand-strong"
          :aria-label="`移除筛选 ${chip.label}`"
          @click="clearField(chip.key)"
        >
          <XIcon class="size-3" aria-hidden="true" />
        </button>
      </BadgePro>
    </div>
  </div>
</template>
