<script setup lang="ts">
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import {
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { computed, watch } from 'vue'

/**
 * 可复用「工人选择器」。选项来自工人目录（IAM 用户），显示姓名（工号 · 部门），
 * 绑定值为内部 userId——UI 只呈现人员姓名 / 工号，userId 不直接暴露给用户输入。
 * 关键词走服务端检索（useBusinessWorkers 的 keyword 过滤）。
 */
const model = defineModel<string>({ default: '' })

const props = defineProps<{
  id?: string
  placeholder?: string
  keepOutOfRange?: boolean
}>()

const { workers, workersPending, filters } = useBusinessWorkers()

const keyword = computed({
  get: () => filters.keyword ?? '',
  set: (value: string) => {
    filters.keyword = value.trim() ? value.trim() : undefined
  },
})

const options = computed(() =>
  workers.value
    .filter((worker) => Boolean(worker.userId))
    .map((worker) => {
      const employeeNo = worker.employeeNo ? worker.employeeNo : ''
      const department = worker.department ? worker.department : ''
      const suffixParts = [employeeNo, department].filter(Boolean)
      const suffix = suffixParts.length > 0 ? `（${suffixParts.join(' · ')}）` : ''
      return {
        // displayName 缺失时降级为「未命名工人」，绝不把内部 userId 当展示名暴露。
        value: worker.userId as string,
        label: `${worker.displayName || '未命名工人'}${suffix}`,
      }
    }),
)

// Most consumers must not retain a worker outside the active result set. Completion forms opt in
// to preserving a planned/selected technician while server-side search or pagination changes.
watch(options, (list) => {
  if (!props.keepOutOfRange && model.value && !list.some((option) => option.value === model.value)) {
    model.value = ''
  }
})

</script>

<template>
  <NvSelect v-model="model">
    <NvSelectTrigger :id="id">
      <NvSelectValue :placeholder="placeholder ?? '请选择工人'" />
    </NvSelectTrigger>
    <NvSelectContent>
      <div class="p-2">
        <NvInput
          v-model="keyword"
          autocomplete="off"
          placeholder="搜索姓名 / 工号 / 部门"
          @keydown.stop
        />
      </div>
      <p v-if="workersPending" class="px-3 py-2 text-sm text-muted-foreground">加载工人中…</p>
      <p v-else-if="options.length === 0" class="px-3 py-2 text-sm text-muted-foreground">
        未找到工人，可调整搜索词。
      </p>
      <NvSelectItem v-for="option in options" :key="option.value" :value="option.value">
        {{ option.label }}
      </NvSelectItem>
    </NvSelectContent>
  </NvSelect>
</template>
