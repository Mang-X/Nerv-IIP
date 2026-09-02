<script setup lang="ts">
/**
 * 多选实体选择器：在 `NvEntityPicker`（单选）之上拼出「选一个加一个」的多选交互。
 *
 * 为什么放在应用侧：`NvEntityPicker` 是单选实体选择弹窗，多选是业务表单的组合用法
 * （询价选多家供应商、设备可用窗口选多个工作中心），不进 `@nerv-iip/ui`。
 *
 * 对外契约刻意保持成**逗号分隔字符串**：这些字段的提交体本来就按逗号/空格切分，
 * 换控件不改提交体，页面侧的校验与 payload 组装一行都不用动。
 */
import { NvBadge, NvEntityPicker, type EntityPickerOption } from '@nerv-iip/ui'
import { XIcon } from '@lucide/vue'
import { computed, shallowRef, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    /** 逗号分隔的已选编码串（提交体原样使用）。 */
    modelValue?: string
    options: EntityPickerOption[]
    /** 选择弹窗标题，如「选择供应商」。 */
    title: string
    placeholder?: string
    /** 底部数据来源说明。 */
    sourceText?: string
    /** 目录为空时的指路文案。 */
    emptyText?: string
    loading?: boolean
    disabled?: boolean
    id?: string
    ariaLabel?: string
    /** 校验未通过时给选择器描红（与输入框的红框保持一致）。 */
    invalid?: boolean
    /** 已选标签区的空态文案。 */
    selectionEmptyText?: string
    /** 服务端搜索时由调用方持有的搜索词。 */
    search?: string
    /** 目录超过一页时打开；候选由服务端按 `search` 过滤。 */
    serverSearch?: boolean
    /** 当前搜索命中的服务端总数。 */
    totalCount?: number
    searchPlaceholder?: string
  }>(),
  {
    modelValue: '',
    placeholder: '请选择',
    loading: false,
    disabled: false,
    invalid: false,
    selectionEmptyText: '尚未选择',
    serverSearch: false,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'update:search', value: string): void
}>()

const selectedCodes = computed(() =>
  (props.modelValue ?? '')
    .split(',')
    .map((code) => code.trim())
    .filter(Boolean),
)

// 服务端搜索会不断替换结果窗。缓存组件见过的人读标签，使已选项在换词后仍可核对和移除；
// 未见过的历史值仍按既有语义显示编码本身。
const optionLabels = shallowRef(new Map<string, string>())
watch(
  () => props.options,
  (options) => {
    const next = new Map(optionLabels.value)
    for (const option of options) next.set(option.value, option.label)
    optionLabels.value = next
  },
  { immediate: true },
)

/** 已选项按选择顺序展示；目录曾返回名称就显示名称，否则显示编码本身。 */
const selectedEntries = computed(() =>
  selectedCodes.value.map((code) => ({
    code,
    label: optionLabels.value.get(code) ?? code,
  })),
)

/** 已选的从候选里摘掉，避免重复添加。 */
const availableOptions = computed(() =>
  props.options.filter((option) => !selectedCodes.value.includes(option.value)),
)

function commit(codes: string[]) {
  emit('update:modelValue', codes.join(','))
}

function add(code: string) {
  const trimmed = code.trim()
  if (!trimmed || selectedCodes.value.includes(trimmed)) return
  commit([...selectedCodes.value, trimmed])
}

function remove(code: string) {
  commit(selectedCodes.value.filter((selected) => selected !== code))
}
</script>

<template>
  <div class="grid gap-2">
    <!-- 选择器自身不留值：选中即入列表，随后清空以便继续选下一个。 -->
    <NvEntityPicker
      :id="id"
      model-value=""
      :options="availableOptions"
      :title="title"
      :placeholder="placeholder"
      :source-text="sourceText"
      :empty-text="emptyText"
      :loading="loading"
      :disabled="disabled"
      :aria-label="ariaLabel"
      :search="search"
      :server-search="serverSearch"
      :total-count="totalCount"
      :search-placeholder="searchPlaceholder"
      :class="invalid ? '[&>button]:border-destructive' : undefined"
      @update:model-value="add"
      @update:search="emit('update:search', $event)"
    />
    <div v-if="selectedEntries.length" class="flex flex-wrap gap-1.5">
      <NvBadge
        v-for="entry in selectedEntries"
        :key="entry.code"
        variant="neutral"
        class="max-w-56 pr-1 pl-2.5"
      >
        <span class="truncate">{{ entry.label }}</span>
        <button
          type="button"
          class="flex size-4 shrink-0 items-center justify-center rounded-sm text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
          :aria-label="`移除 ${entry.label}`"
          :disabled="disabled"
          @click="remove(entry.code)"
        >
          <XIcon class="size-3" aria-hidden="true" />
        </button>
      </NvBadge>
    </div>
    <p v-else class="text-xs text-muted-foreground">{{ selectionEmptyText }}</p>
  </div>
</template>
