<script setup lang="ts">
import { useMesWorkScopeSelection } from '@/composables/useBusinessMes'
import { NvMobileDropdownMenu, NvMobileDropdownMenuItem } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

/**
 * PDA MES 作业范围选择入口（#1297，与 Console 侧 #1296 同一姿势的移动端形态）。
 *
 * 后端 work-context 按设计不替用户选范围：不带 scopeKind/scopeId 只回授权清单，
 * `selectedScope` 恒空。此前 PDA 没有任何选择入口，工序执行/报工的读查询被 scope gate
 * 永久拦在 enabled=false。本组件把授权清单渲染成选择器：composable 侧已自动兜底选清单
 * 第一项（或记住的选择），这里负责让操作工看见当前范围并能换；显式切换会记住（localStorage）。
 *
 * 形态取移动端惯例：整宽下拉筛选条（与 WMS `WmsScopeStatusFilter` 同一模式），
 * 触发行 44px、面板行 48px，单手拇指可达；不用 PC 端的 NvSelect 弹层。
 *
 * 选择按 principal/org/env 全 PDA 共享——这里切了范围，工序执行与报工同步生效。
 */
const props = defineProps<{
  /** 用于解析授权范围的权限码（如 business.mes.operations.read）。 */
  permissionCode: string
}>()

const scope = useMesWorkScopeSelection(props.permissionCode)
const hasOptions = computed(() => scope.scopeOptions.value.length > 0)
// DropdownMenuItem 的 model 是 string | number；这里按 PDA 房规用可写 computed 桥接，
// 空值一律归一成 undefined（不把空串写进共享选择）。
const selection = computed<string | number | undefined>({
  get: () => scope.scopeSelectionValue.value,
  set: (value) => {
    scope.scopeSelectionValue.value = value ? String(value) : undefined
  },
})
</script>

<template>
  <div v-if="hasOptions" data-testid="mes-work-scope-select">
    <p class="px-1 pb-1 text-xs text-muted-foreground">作业范围</p>
    <NvMobileDropdownMenu class="rounded-lg border border-border">
      <NvMobileDropdownMenuItem
        v-model="selection"
        title="选择作业范围"
        :options="scope.scopeOptions.value"
      />
    </NvMobileDropdownMenu>
  </div>
</template>
