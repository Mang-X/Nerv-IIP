<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
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
  toast,
} from '@nerv-iip/ui'
import { CircleSlashIcon, EyeIcon, PencilIcon, PlayIcon } from 'lucide-vue-next'
import { ref } from 'vue'

export interface DetailField {
  label: string
  value: string
}

const props = defineProps<{
  /** 该行列表项（含 typed 字段）。 */
  row: BusinessConsoleResourceItem
  /** 业务名称，用于弹窗标题与提示。 */
  entityLabel: string
  /** 详情弹窗展示的字段（业务中文 label + 取自行的值）。 */
  detailFields: DetailField[]
  /** 来自 useMasterDataResourceActions 的动作集合（停用/启用；编辑由页面自带表单处理）。 */
  actions: {
    disable: (code: string) => Promise<unknown>
    enable: (code: string) => Promise<unknown>
    disablePending: { value: boolean }
    enablePending: { value: boolean }
  }
}>()

// 「编辑」交由页面打开各自的全字段表单（带回填），故此处只发事件，不在组件内编辑。
const emit = defineEmits<{ edit: [row: BusinessConsoleResourceItem] }>()

const detailOpen = ref(false)
const toggleOpen = ref(false)

function formatError(error: unknown) {
  return error instanceof Error ? error.message : '请求失败，请稍后重试。'
}

async function confirmToggle() {
  const code = props.row.code
  if (!code) return
  const isActive = props.row.active !== false
  try {
    if (isActive) {
      await props.actions.disable(code)
      toast.success('已停用')
    } else {
      await props.actions.enable(code)
      toast.success('已启用')
    }
    toggleOpen.value = false
  } catch (error) {
    toast.error(formatError(error))
  }
}
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
    <NvDropdownMenuItem :disabled="!row.code" @click="toggleOpen = true">
      <CircleSlashIcon v-if="row.active !== false" aria-hidden="true" />
      <PlayIcon v-else aria-hidden="true" />
      {{ row.active !== false ? '停用' : '启用' }}
    </NvDropdownMenuItem>
  </NvRowActions>

  <!-- 查看详情（只读） -->
  <NvDialog v-model:open="detailOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ entityLabel }}详情</NvDialogTitle>
        <NvDialogDescription
          >{{ row.displayName ?? row.code ?? '' }} 的关键信息。</NvDialogDescription
        >
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

  <!-- 停用 / 启用 二次确认 -->
  <NvAlertDialog v-model:open="toggleOpen">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>
          {{ row.active !== false ? `确认停用该${entityLabel}？` : `确认启用该${entityLabel}？` }}
        </NvAlertDialogTitle>
        <NvAlertDialogDescription>
          {{
            row.active !== false
              ? '停用后将不能用于新建/计划，已有记录不受影响。'
              : '启用后可重新用于新建与计划。'
          }}
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvAlertDialogAction
          :disabled="actions.disablePending.value || actions.enablePending.value"
          @click="confirmToggle"
        >
          {{ row.active !== false ? '确认停用' : '确认启用' }}
        </NvAlertDialogAction>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
