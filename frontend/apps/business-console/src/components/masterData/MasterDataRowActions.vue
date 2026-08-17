<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvRowActions,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { CircleSlashIcon, EyeIcon, PencilIcon, PlayIcon } from '@lucide/vue'
import { computed, ref } from 'vue'

export interface DetailField {
  label: string
  value: string
}

const props = defineProps<{
  /** 该行列表项（含 typed 字段）。 */
  row: BusinessConsoleResourceItem
  /** 业务名称，用于行操作可读标签与详情弹窗标题。 */
  entityLabel: string
  /** 详情弹窗展示的字段（业务中文 label + 取自行的值）。 */
  detailFields: DetailField[]
}>()

/**
 * 「编辑」与「停用/启用」都只发事件、不在组件内落地：
 * - 编辑交由页面打开各自的全字段表单（带回填）；
 * - 停用/启用交由**页面层单实例确认框**收集原因并提交（#1591）。确认框此前装在本组件内部，
 *   随行渲染成 N 个实例，违反 `confirm-destroy.md` 规则 5。
 */
const emit = defineEmits<{
  edit: [row: BusinessConsoleResourceItem]
  toggle: [row: BusinessConsoleResourceItem]
}>()

const detailOpen = ref(false)
const isActive = computed(() => props.row.active !== false)
const actionLabel = computed(() => (isActive.value ? '停用' : '启用'))
</script>

<template>
  <NvRowActions :label="`${entityLabel}操作 ${row.code ?? ''}`">
    <NvDropdownMenuItem @click="detailOpen = true">
      <EyeIcon aria-hidden="true" />
      查看详情
    </NvDropdownMenuItem>
    <NvDropdownMenuItem :disabled="!row.code" @click="emit('edit', row)">
      <PencilIcon aria-hidden="true" />
      编辑
    </NvDropdownMenuItem>
    <NvDropdownMenuItem :disabled="!row.code" @click="emit('toggle', row)">
      <CircleSlashIcon v-if="isActive" aria-hidden="true" />
      <PlayIcon v-else aria-hidden="true" />
      {{ actionLabel }}
    </NvDropdownMenuItem>
  </NvRowActions>

  <!-- 查看详情（只读，字段少、即看即关）——按 interaction-patterns.md §3 属合规的轻详情载体。 -->
  <NvDialog v-model:open="detailOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ entityLabel }}详情</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          row.displayName ?? row.code ?? ''
        }}</NvDialogDescription>
      </NvDialogHeader>
      <dl class="grid gap-3 sm:grid-cols-2">
        <div v-for="field in detailFields" :key="field.label" class="grid gap-1">
          <dt class="text-xs text-muted-foreground">{{ field.label }}</dt>
          <dd class="text-sm">{{ field.value || '无' }}</dd>
        </div>
        <div class="grid gap-1">
          <dt class="text-xs text-muted-foreground">状态</dt>
          <dd><NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" /></dd>
        </div>
      </dl>
      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="detailOpen = false">关闭</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
