<script setup lang="ts">
import { computed } from 'vue'

/**
 * 「带出式录入」的只读上下文区（业务弹窗通用样板）。
 *
 * 用法约束（见 business-console/AGENTS.md §1.5-B）：
 * - 凡是能从所选行带出的字段（单号 / 工序 / 工作中心 / 物料 / 计划数量 …），一律进这里**只读展示**，
 *   不做成 `readonly` 的输入框——只读输入框看起来仍像输入位，一线会去点、去改、去疑惑。
 * - 视觉上与录入区分层：本区是 muted 卡片 + `<dl>`，录入区是常规 `NvField`。
 * - 空值项直接不渲染，避免整片「—」噪音。
 */
const props = defineProps<{
  /** 只读上下文条目；value 为空的条目不渲染。 */
  items: { label: string; value?: string | number | null }[]
  /** 区块的无障碍名称（如「报工对象」）。 */
  label: string
}>()

const visibleItems = computed(() =>
  props.items.filter((item) => {
    if (item.value === null || item.value === undefined) return false
    return String(item.value).trim().length > 0
  }),
)
</script>

<template>
  <section
    v-if="visibleItems.length"
    :aria-label="label"
    data-slot="carried-context"
    class="rounded-lg border border-border bg-muted/40 px-4 py-3"
  >
    <dl class="grid gap-x-6 gap-y-2.5 sm:grid-cols-2">
      <div v-for="item in visibleItems" :key="item.label" class="grid gap-0.5">
        <dt class="text-xs text-muted-foreground">{{ item.label }}</dt>
        <dd class="text-sm font-medium text-foreground">{{ item.value }}</dd>
      </div>
    </dl>
  </section>
</template>
