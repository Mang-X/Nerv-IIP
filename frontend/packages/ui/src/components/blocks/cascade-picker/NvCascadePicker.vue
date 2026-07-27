<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import NvSearchSelect, {
  type SearchSelectOption,
} from '../../pc/combobox/NvSearchSelect.vue'

/**
 * Blocks — 级联选择器：一行多级依赖选择（如 车间 → 产线 → 设备），每级都是
 * 可搜索的弹出单选。选中上级会**自动清空所有下游层级**；每级第一项固定为
 * 「全部」（值 = 空串），代表不在该层收窄。层级的选项过滤（如按车间过滤产线）
 * 由调用方根据已选值组装 `options` 完成——组件只负责选择交互与联动清空。
 */
export interface CascadePickerLevel {
  /** 层级键（在 modelValue 里的字段名）。 */
  key: string
  /** 层级名称（展示在选择框上方）。 */
  label: string
  options: SearchSelectOption[]
  placeholder?: string
  /** 「全部」项文案；缺省「全部」。 */
  allLabel?: string
  loading?: boolean
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    /** 各层级选中值；空串 = 全部（未在该层收窄）。 */
    modelValue: Record<string, string>
    levels: CascadePickerLevel[]
    class?: HTMLAttributes['class']
  }>(),
  {},
)

const emit = defineEmits<{ (e: 'update:modelValue', value: Record<string, string>): void }>()

const levelOptions = computed(() =>
  props.levels.map((level) => [
    { value: '', label: level.allLabel ?? '全部' },
    ...level.options,
  ]),
)

function onPick(index: number, value: string) {
  const next: Record<string, string> = { ...props.modelValue }
  next[props.levels[index]!.key] = value
  // 上级变化后，下游层级的已选值不再有意义，一律清空回「全部」。
  for (const level of props.levels.slice(index + 1)) next[level.key] = ''
  emit('update:modelValue', next)
}
</script>

<template>
  <div
    :class="cn('flex flex-wrap items-end gap-3', props.class)"
    data-slot="nv-cascade-picker"
    role="group"
  >
    <div v-for="(level, index) in levels" :key="level.key" class="min-w-44 flex-1 space-y-1.5">
      <span class="block text-xs font-medium text-muted-foreground">{{ level.label }}</span>
      <NvSearchSelect
        :model-value="modelValue[level.key] ?? ''"
        :options="levelOptions[index]!"
        :placeholder="level.placeholder ?? (level.allLabel ?? '全部')"
        :search-placeholder="`搜索${level.label}…`"
        :aria-label="level.label"
        :loading="level.loading"
        :disabled="level.disabled"
        @update:model-value="onPick(index, $event)"
      />
    </div>
  </div>
</template>
