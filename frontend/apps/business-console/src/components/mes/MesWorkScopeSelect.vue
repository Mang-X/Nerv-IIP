<script setup lang="ts">
import { useMesWorkScopeSelection } from '@/composables/mes'
import {
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { computed } from 'vue'

/**
 * MES 作业范围选择入口（#1288）。
 *
 * 后端 work-context 按设计不替用户选范围：不带 scopeKind/scopeId 只回授权清单，
 * `selectedScope` 恒空。此前 Console 没有任何选择入口，所有 MES 读查询被 scope gate
 * 永久拦在 enabled=false。本组件把授权清单渲染成选择器：composable 侧已自动兜底
 * 选择清单第一项，这里负责让用户看见当前范围并可切换；显式切换会记住（localStorage）。
 *
 * 选择按 principal/org/env 全 Console 共享——这里切了范围，工单列表/详情/工序任务/
 * 排产待排池同步生效。
 */
const props = defineProps<{
  /** 用于解析授权范围的权限码（如 business.mes.work-orders.read）。 */
  permissionCode: string
  readFailed?: boolean
  readPending?: boolean
}>()

const scope = useMesWorkScopeSelection(props.permissionCode)
const statusMessage = computed(() => {
  if (props.readFailed || scope.scopeReadState.value === 'error') return '范围读取失败'
  if (scope.scopeReadState.value === 'idle') return '等待组织与环境就绪'
  if (props.readPending || scope.scopePending.value) return '正在读取作业范围…'
  if (scope.scopeUnavailable.value) return '无可选授权范围'
  return ''
})
</script>

<template>
  <div class="flex max-w-full items-center gap-2" data-testid="mes-work-scope-select">
    <span class="shrink-0 whitespace-nowrap text-sm text-muted-foreground">作业范围</span>
    <span v-if="statusMessage" class="text-sm text-muted-foreground" role="status">
      {{ statusMessage }}
    </span>
    <NvSelect
      v-else
      v-model="scope.scopeSelectionValue.value"
      :disabled="scope.scopeOptions.value.length === 0"
    >
      <NvSelectTrigger class="h-9 min-w-40" aria-label="作业范围">
        <NvSelectValue placeholder="选择作业范围" />
      </NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem
          v-for="option in scope.scopeOptions.value"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </NvSelectItem>
      </NvSelectContent>
    </NvSelect>
  </div>
</template>
