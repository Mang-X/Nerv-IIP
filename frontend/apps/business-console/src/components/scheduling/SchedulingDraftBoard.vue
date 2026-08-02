<script setup lang="ts">
import {
  GanttChart,
  ResourceSchedulerBoard,
  type ScheduleModel,
  type TaskDragPayload,
} from '@nerv-iip/scheduling'
import type { WorkingSchedulePendingOperation } from '@/composables/useWorkingScheduleDraft'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import {
  NvButton,
  NvInput,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import { computed, shallowRef } from 'vue'

const props = defineProps<{
  model?: ScheduleModel
  pendingOperations?: WorkingSchedulePendingOperation[]
  readOnly?: boolean
  /**
   * 本次会话内落库成功的 override 工序键（`orderId:operationId`）。
   * 仅为会话内乐观回显：override 目前没有读接口，刷新或换会话后无法回读，徽标即消失。
   * followUp: 待后端补 override 查询 facade 后，改为服务端回读、跨会话回显。
   */
  persistedOperationKeys?: string[]
  /** 持久化请求进行中（禁用所有持久锁定按钮，避免并发重复提交）。 */
  persistPending?: boolean
}>()
const emit = defineEmits<{
  move: [payload: TaskDragPayload]
  update: [taskId: string, patch: { resourceId?: string; startUtc?: string; endUtc?: string }]
  lock: [taskId: string, locked: boolean]
  lockedAttempt: [taskId: string]
  moveToPending: [taskId: string]
  restorePending: [taskId: string]
  persistOverride: [taskId: string]
}>()
const view = shallowRef('gantt')
// 物料风险（软约束）：已排但缺料的工序，开工前必须先备料。
const materialRisks = computed(() => props.model?.materialRisks ?? [])
// 设备数据风险（软约束）：排在状态未知设备上的工序，开工前需人工确认设备可用。
const equipmentRisks = computed(() => props.model?.equipmentRisks ?? [])
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
              ·
              {{
                item.message ||
                (item.reasonCode
                  ? describeScheduleInvalidationReason(item.reasonCode)
                  : '待重新排程')
              }}
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
    <!--
      物料风险横幅：齐套是开工门槛不是排产门槛。缺料工单照排进方案，
      这里显式告诉规划员「哪些工序开工前必须先备料」，避免拿着方案去发布却被 MES 齐套门拦下。
    -->
    <section
      v-if="materialRisks.length"
      class="grid gap-1.5 rounded-md border border-warning/40 bg-warning/10 px-3 py-2.5 text-sm"
      data-testid="scheduling-material-risks"
    >
      <p class="font-semibold">{{ materialRisks.length }} 道工序有物料风险 · 需在开工前完成备料</p>
      <ul class="grid gap-1 text-xs">
        <li v-for="risk in materialRisks" :key="`${risk.orderId}:${risk.operationId}`">
          {{ risk.orderId }} · {{ risk.operationId }} —
          <template v-if="risk.shortages.length">
            {{ risk.shortages.map((s) => `${s.materialId} 缺 ${s.shortageQuantity}`).join('、') }}
          </template>
          <template v-else>{{ risk.message }}</template>
        </li>
      </ul>
    </section>
    <!--
      设备数据风险横幅：「不知道」不等于「不可用」。无快照/快照过期的设备照排，
      但必须显式告诉规划员哪些工序的设备状态是盲区，开工前要人工确认。
    -->
    <section
      v-if="equipmentRisks.length"
      class="grid gap-1.5 rounded-md border border-border bg-muted/40 px-3 py-2.5 text-sm"
      data-testid="scheduling-equipment-risks"
    >
      <p class="font-semibold">
        {{ equipmentRisks.length }} 道工序的设备状态未知 · 开工前请人工确认设备可用
      </p>
      <ul class="grid gap-1 text-xs text-muted-foreground">
        <li v-for="risk in equipmentRisks" :key="`${risk.orderId}:${risk.operationId}`">
          {{ risk.orderId }} · {{ risk.operationId }} — {{ risk.message }}
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
          <thead class="sticky top-0 bg-muted text-left">
            <tr>
              <th class="p-2">工单 / 工序</th>
              <th class="p-2">资源</th>
              <th class="p-2">开始</th>
              <th class="p-2">结束</th>
              <th class="p-2">物料</th>
              <th class="p-2">设备状态</th>
              <th class="p-2">锁定</th>
              <th class="p-2">待排</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="task in model.tasks.filter(
                (item) => item.type === 'operation' && !item.blockKind,
              )"
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
                <span
                  v-if="task.materialRisk"
                  class="inline-flex items-center rounded border border-warning/50 bg-warning/10 px-1.5 text-xs font-semibold text-warning"
                  :title="task.materialRisk.message"
                  >缺料待备</span
                >
                <span v-else class="text-xs text-muted-foreground">齐套</span>
              </td>
              <td class="p-2">
                <span
                  v-if="task.equipmentRisk"
                  class="inline-flex items-center rounded border border-border bg-muted px-1.5 text-xs font-semibold text-muted-foreground"
                  :title="task.equipmentRisk.message"
                  >状态未知</span
                >
                <span v-else class="text-xs text-muted-foreground">正常</span>
              </td>
              <td class="p-2">
                <div class="flex flex-wrap items-center gap-1.5">
                  <NvButton
                    size="sm"
                    :variant="task.locked ? 'secondary' : 'outline'"
                    type="button"
                    :disabled="readOnly"
                    @click="emit('lock', task.id, !task.locked)"
                    >{{ task.locked ? '解锁' : '锁定' }}</NvButton
                  >
                  <NvButton
                    size="sm"
                    variant="outline"
                    type="button"
                    :disabled="readOnly || persistPending || !task.resourceId"
                    :title="
                      task.resourceId
                        ? '把该工序的资源与起止落库为跨方案 override，重排程自动继承'
                        : '该工序未分配资源，先指定资源再持久锁定'
                    "
                    @click="emit('persistOverride', task.id)"
                    >持久锁定</NvButton
                  >
                  <NvStatusBadge
                    v-if="persistedOperationKeys?.includes(`${task.orderId}:${task.operationId}`)"
                    label="本次会话已持久化"
                    tone="success"
                    title="该工序 override 已在本次会话内落库；override 暂无读接口，刷新后徽标不再回显，但落库结果仍会被重排程继承"
                  />
                </div>
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
