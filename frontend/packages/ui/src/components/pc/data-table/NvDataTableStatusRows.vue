<script setup lang="ts">
import { ListFilterIcon, RotateCcwIcon, SearchIcon, TriangleAlertIcon } from '@lucide/vue'
import { TableCell, TableRow } from '../../ui/table'
import NvButton from '../button/NvButton.vue'

defineProps<{
  bodyState: 'error' | 'awaiting' | 'empty'
  colSpan: number
  error?: unknown
  resolvedErrorMessage: string
  loading: boolean
  awaitingScopeMessage: string
  emptyMessage: string
  hasActiveSearch: boolean
}>()
const emit = defineEmits<{ retry: []; reset: [] }>()
</script>

<template>
  <!-- 失败：红色警示 + 具体原因 + 重试。此处禁止出现任何安慰性措辞 -->
  <template v-if="bodyState === 'error'">
    <TableRow class="hover:bg-transparent">
      <TableCell :colspan="colSpan" class="h-40 p-0">
        <!-- role="alert"（= assertive + atomic 的活动区域）承担读屏播报：
                     列表已渲染时分页失败，错误面板是唯一变化的部分，不播报读屏用户
                     察觉不到。此前由各页面手写错误行携带，页面改接组件一等 error
                     态后无人承担。包住整个插槽，调用点自定义呈现同样被播报。 -->
        <div role="alert" class="flex flex-col items-center justify-center gap-2 py-10 text-center">
          <slot name="error" :message="resolvedErrorMessage" :error="error">
            <div class="nv-dt-state-icon nv-dt-state-icon--error">
              <TriangleAlertIcon class="size-5" aria-hidden="true" />
            </div>
            <p class="text-sm font-medium text-destructive-strong">数据加载失败</p>
            <p class="max-w-md px-6 text-xs text-muted-foreground">
              {{ resolvedErrorMessage }}
            </p>
            <NvButton
              variant="outline"
              size="sm"
              class="mt-1"
              :disabled="loading"
              @click="emit('retry')"
            >
              <RotateCcwIcon aria-hidden="true" />
              {{ loading ? '重试中…' : '重新加载' }}
            </NvButton>
          </slot>
        </div>
      </TableCell>
    </TableRow>
  </template>

  <!-- 未查询：还没查，不是没有数据。只说下一步该选什么 -->
  <template v-else-if="bodyState === 'awaiting'">
    <TableRow class="hover:bg-transparent">
      <TableCell :colspan="colSpan" class="h-40 p-0">
        <div class="flex flex-col items-center justify-center gap-2 py-10 text-center">
          <slot name="awaiting">
            <div class="nv-dt-state-icon nv-dt-state-icon--awaiting">
              <ListFilterIcon class="size-5" aria-hidden="true" />
            </div>
            <p class="text-sm font-medium text-foreground">尚未发起查询</p>
            <p class="max-w-md px-6 text-xs text-muted-foreground">
              {{ awaitingScopeMessage }}
            </p>
          </slot>
        </div>
      </TableCell>
    </TableRow>
  </template>

  <!-- Empty -->
  <template v-else>
    <TableRow class="hover:bg-transparent">
      <TableCell :colspan="colSpan" class="h-40 p-0">
        <div class="flex flex-col items-center justify-center gap-2 py-10 text-center">
          <slot name="empty">
            <div
              class="flex size-10 items-center justify-center rounded-full bg-muted text-muted-foreground"
            >
              <SearchIcon class="size-5" aria-hidden="true" />
            </div>
            <p class="text-sm text-muted-foreground">
              {{ hasActiveSearch ? '没有匹配的结果' : emptyMessage }}
            </p>
            <button
              v-if="hasActiveSearch"
              type="button"
              class="text-xs text-brand-strong underline-offset-2 hover:underline"
              @click="emit('reset')"
            >
              清除筛选条件
            </button>
          </slot>
        </div>
      </TableCell>
    </TableRow>
  </template>
</template>

<style scoped>
@layer nv-components {
  /* 表体状态图章：空 / 失败 / 未查询三态在颜色与轮廓上就分得开，
     不靠读文案区分（失败 = 警示描边的红章，未查询 = 虚线待办章）。 */
  .nv-dt-state-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 2.5rem;
    height: 2.5rem;
    border-radius: 9999px;
  }
  .nv-dt-state-icon--error {
    background-color: color-mix(in oklch, var(--destructive) 12%, transparent);
    border: 1px solid color-mix(in oklch, var(--destructive) 30%, transparent);
    color: var(--destructive);
  }
  .nv-dt-state-icon--awaiting {
    background-color: transparent;
    border: 1px dashed var(--border);
    color: var(--muted-foreground);
  }
}
</style>
