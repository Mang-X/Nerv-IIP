<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { NvListRow, NvMobileButton, NvMobileTag, NvSearchBar } from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import RetryableListError from '@/components/RetryableListError.vue'
import { operationTaskLabel, operationTaskRowTitle, workOrderLabel } from './operationPresentation'

/**
 * 遥测候选转正的「报工到哪道工序」。目标由父级派生（见 useTelemetryCandidateTarget），
 * 这里只负责展示当前目标、列出候选工序和收集用户的点选。
 */
type Task = BusinessConsoleMesOperationTaskRow

const props = defineProps<{
  deviceAssetId: string
  target: Task | null
  tasks: Task[]
  pending: boolean
  error: unknown
  scopeReady: boolean
}>()
const keyword = defineModel<string>('keyword', { required: true })
const emit = defineEmits<{ choose: [task: Task]; retry: [] }>()

const choosing = ref(false)
const searching = computed(() => keyword.value.trim() !== '')

// 搜索会放开设备限制：其它机台的工序也会列出来，转过去产量和稼动就记到那台设备上，必须醒目提示。
function isOtherDevice(task: Task) {
  return Boolean(props.deviceAssetId) && task.deviceAssetId?.trim() !== props.deviceAssetId
}

function choose(task: Task) {
  emit('choose', task)
  choosing.value = false
}
function cancelChoosing() {
  choosing.value = false
  keyword.value = ''
}
</script>

<template>
  <div class="space-y-2">
    <div
      v-if="target && !choosing"
      class="flex items-center justify-between gap-3 rounded-lg border border-border bg-background px-3 py-2"
    >
      <div class="min-w-0 space-y-1">
        <p class="text-xs text-muted-foreground">报工到</p>
        <p data-testid="telemetry-target" class="font-medium break-words text-foreground">
          {{ operationTaskRowTitle(target) }}
        </p>
        <NvMobileTag
          v-if="target.deviceAssetId && isOtherDevice(target)"
          variant="warning"
          size="sm"
          data-testid="telemetry-target-other-device"
          >非本设备</NvMobileTag
        >
      </div>
      <button
        type="button"
        data-testid="telemetry-change-target"
        class="shrink-0 text-sm text-primary"
        @click="choosing = true"
      >
        改选
      </button>
    </div>
    <template v-else>
      <NvSearchBar v-model="keyword" placeholder="按工单号搜索执行中的工序" />
      <p v-if="!scopeReady" class="text-sm text-muted-foreground">
        报工范围未就绪，暂时无法选择工序。
      </p>
      <RetryableListError
        v-else-if="error"
        :error="error"
        :pending="pending"
        fallback="执行中工序读取失败，请重试。"
        test-id="telemetry-target-error"
        @retry="emit('retry')"
      />
      <p v-else-if="pending" class="text-sm text-muted-foreground">正在加载执行中的工序…</p>
      <p
        v-else-if="tasks.length === 0"
        data-testid="telemetry-target-empty"
        class="text-sm text-muted-foreground"
      >
        {{ searching ? '没有匹配的执行中工序' : '这台设备当前没有执行中的工序，请按工单号搜索' }}
      </p>
      <template v-else>
        <p class="text-xs text-muted-foreground">
          {{ searching ? '选择要报工的工序' : '这台设备当前执行中的工序' }}
        </p>
        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="task in tasks"
            :key="task.operationTaskId"
            :data-testid="`telemetry-target-option-${task.operationTaskId}`"
            :title="workOrderLabel(task)"
            :subtitle="operationTaskLabel(task)"
            @select="choose(task)"
          >
            <template v-if="isOtherDevice(task)" #meta>
              <NvMobileTag
                variant="warning"
                size="sm"
                class="mt-1"
                data-testid="telemetry-target-other-device"
                >非本设备</NvMobileTag
              >
            </template>
          </NvListRow>
        </div>
      </template>
      <NvMobileButton
        v-if="choosing"
        variant="text"
        size="sm"
        data-testid="telemetry-cancel-change"
        @click="cancelChoosing"
        >取消改选</NvMobileButton
      >
    </template>
  </div>
</template>
