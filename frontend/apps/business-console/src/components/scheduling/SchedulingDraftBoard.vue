<script setup lang="ts">
import {
  GanttChart,
  ResourceSchedulerBoard,
  type ScheduleModel,
  type TaskDragPayload,
} from '@nerv-iip/scheduling'
import type { WorkingSchedulePendingOperation } from '@/composables/useWorkingScheduleDraft'
import { NvButton, NvInput, NvTabs, NvTabsContent, NvTabsList, NvTabsTrigger } from '@nerv-iip/ui'
import { shallowRef } from 'vue'

defineProps<{
  model?: ScheduleModel
  pendingOperations?: WorkingSchedulePendingOperation[]
  readOnly?: boolean
}>()
const emit = defineEmits<{
  move: [payload: TaskDragPayload]
  update: [taskId: string, patch: { resourceId?: string; startUtc?: string; endUtc?: string }]
  lock: [taskId: string, locked: boolean]
  lockedAttempt: [taskId: string]
  moveToPending: [taskId: string]
  restorePending: [taskId: string]
}>()
const view = shallowRef('gantt')
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" data-testid="scheduling-draft-board">
    <header>
      <h2 class="font-semibold">排程草案工作区</h2>
      <p class="text-sm text-muted-foreground">甘特拖拽、资源泳道和表格编辑共享同一份草稿状态。</p>
    </header>
    <section
      class="grid gap-2 rounded-md border bg-muted/20 p-3"
      data-testid="operation-pending-pool"
    >
      <div class="flex items-center justify-between gap-2">
        <h3 class="text-sm font-semibold">工序待排池</h3>
        <span class="text-xs text-muted-foreground"
          >{{ pendingOperations?.length ?? 0 }} 道工序</span
        >
      </div>
      <p v-if="!pendingOperations?.length" class="text-sm text-muted-foreground">
        暂无未排、移回或受失效影响的工序。
      </p>
      <ul v-else class="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
        <li
          v-for="item in pendingOperations"
          :key="item.id"
          class="flex items-center justify-between gap-2 rounded-md border bg-card p-2 text-sm"
        >
          <div class="min-w-0">
            <p class="truncate font-medium">{{ item.orderId }} · {{ item.operationId }}</p>
            <p class="truncate text-xs text-muted-foreground">
              {{
                item.source === 'removed'
                  ? '规划员移回'
                  : item.source === 'invalidated'
                    ? '失效影响'
                    : '求解未排'
              }}
              · {{ item.message || item.reasonCode || '待重新排程' }}
            </p>
          </div>
          <NvButton
            v-if="item.canRestore && item.taskId"
            size="sm"
            variant="outline"
            type="button"
            :disabled="readOnly"
            @click="emit('restorePending', item.taskId)"
            >恢复</NvButton
          >
        </li>
      </ul>
    </section>
    <div
      v-if="!model"
      class="flex min-h-48 items-center justify-center rounded-md border border-dashed text-sm text-muted-foreground"
    >
      选择待排工单并生成首版方案后开始编辑。
    </div>
    <NvTabs v-else v-model="view">
      <NvTabsList>
        <NvTabsTrigger value="gantt">工单甘特</NvTabsTrigger>
        <NvTabsTrigger value="resource">资源排产板</NvTabsTrigger>
        <NvTabsTrigger value="table">表格编辑</NvTabsTrigger>
      </NvTabsList>
      <NvTabsContent value="gantt" class="h-[34rem] overflow-hidden rounded-md border">
        <GanttChart
          :model="model"
          :read-only="readOnly"
          @task-drag-end="emit('move', $event)"
          @locked-drag-attempt="emit('lockedAttempt', $event)"
        />
      </NvTabsContent>
      <NvTabsContent value="resource" class="h-[34rem] overflow-hidden rounded-md border">
        <ResourceSchedulerBoard
          :model="model"
          :read-only="readOnly"
          @task-drag-end="emit('move', $event)"
          @locked-drag-attempt="emit('lockedAttempt', $event)"
        />
      </NvTabsContent>
      <NvTabsContent value="table" class="max-h-[34rem] overflow-auto rounded-md border">
        <table class="w-full text-sm">
          <thead class="sticky top-0 bg-muted/90 text-left">
            <tr>
              <th class="p-2">工单 / 工序</th>
              <th class="p-2">资源</th>
              <th class="p-2">开始</th>
              <th class="p-2">结束</th>
              <th class="p-2">锁定</th>
              <th class="p-2">待排</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="task in model.tasks.filter((item) => item.type === 'operation')"
              :key="task.id"
              class="border-t"
            >
              <td class="p-2 font-medium">{{ task.orderId }} · {{ task.operationId }}</td>
              <td class="p-2">
                <NvInput
                  class="h-8 min-w-32"
                  :disabled="readOnly || task.locked"
                  :model-value="task.resourceId"
                  @update:model-value="emit('update', task.id, { resourceId: String($event) })"
                />
              </td>
              <td class="p-2">
                <NvInput
                  class="h-8 min-w-48"
                  :disabled="readOnly || task.locked"
                  :model-value="task.startUtc"
                  @update:model-value="emit('update', task.id, { startUtc: String($event) })"
                />
              </td>
              <td class="p-2">
                <NvInput
                  class="h-8 min-w-48"
                  :disabled="readOnly || task.locked"
                  :model-value="task.endUtc"
                  @update:model-value="emit('update', task.id, { endUtc: String($event) })"
                />
              </td>
              <td class="p-2">
                <NvButton
                  size="sm"
                  :variant="task.locked ? 'secondary' : 'outline'"
                  type="button"
                  :disabled="readOnly"
                  @click="emit('lock', task.id, !task.locked)"
                  >{{ task.locked ? '解锁' : '锁定' }}</NvButton
                >
              </td>
              <td class="p-2">
                <NvButton
                  size="sm"
                  variant="ghost"
                  type="button"
                  :disabled="readOnly || task.locked"
                  @click="emit('moveToPending', task.id)"
                  >移回待排</NvButton
                >
              </td>
            </tr>
          </tbody>
        </table>
      </NvTabsContent>
    </NvTabs>
  </section>
</template>
