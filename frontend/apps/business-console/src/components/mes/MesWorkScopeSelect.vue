<script setup lang="ts">
import { useMesWorkScopeSelection } from '@/composables/useBusinessMes'
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
}>()

const scope = useMesWorkScopeSelection(props.permissionCode)
const selection = scope.scopeSelectionValue
const hasOptions = computed(() => scope.scopeOptions.value.length > 0)
</script>

<template>
  <div class="flex items-center gap-2" data-testid="mes-work-scope-select">
    <span class="text-sm text-muted-foreground">作业范围</span>
    <NvSelect v-model="selection" :disabled="!hasOptions">
      <NvSelectTrigger class="h-9 min-w-40" aria-label="作业范围">
        <NvSelectValue :placeholder="hasOptions ? '选择作业范围' : '无可选授权范围'" />
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
