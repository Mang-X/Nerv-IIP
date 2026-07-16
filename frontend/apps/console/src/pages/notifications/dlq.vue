<script setup lang="ts">
import type { NotificationDeadLetterResponse } from '@nerv-iip/api-client'
import { useNotificationDeadLetters } from '@/composables/useNotificationDeadLetters'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Button, NvPageHeader, NvSectionCard, NvSectionCards, toast } from '@nerv-iip/ui'
import { BanIcon, PlayIcon, RefreshCwIcon, RotateCwIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '死信队列',
  },
})

const {
  allError,
  actionableCount,
  consumerNameFilter,
  deadLetters,
  detailPending,
  eventTypeFilter,
  failedCount,
  ignore,
  ignorePending,
  listPending,
  metricsPending,
  pendingCount,
  refreshDeadLetters,
  replay,
  replayBatchPending,
  replayFiltered,
  replayPending,
  selectedDeadLetter,
  selectedDeadLetterId,
  statusFilter,
} = useNotificationDeadLetters()

const ignoreReason = ref('')
const actionPending = computed(
  () => replayPending.value || replayBatchPending.value || ignorePending.value,
)
const refreshPending = computed(() => listPending.value || metricsPending.value)
const errorMessage = computed(() => allError.value?.message ?? '')
const canReplaySelected = computed(
  () => selectedDeadLetter.value && selectedDeadLetter.value.status !== 'Replayed',
)
const canIgnoreSelected = computed(
  () =>
    selectedDeadLetter.value &&
    selectedDeadLetter.value.status !== 'Ignored' &&
    ignoreReason.value.trim().length > 0,
)
const ignoreHandledError = (_error: unknown) => {}

watch(
  deadLetters,
  (items) => {
    if (!selectedDeadLetterId.value && items[0]?.id) {
      selectedDeadLetterId.value = items[0].id
    }
    if (
      selectedDeadLetterId.value &&
      !items.some((item) => item.id === selectedDeadLetterId.value)
    ) {
      selectedDeadLetterId.value = items[0]?.id
    }
  },
  { immediate: true },
)

watch(selectedDeadLetterId, () => {
  ignoreReason.value = ''
})

async function handleRefresh() {
  await refreshDeadLetters().catch(ignoreHandledError)
}

async function handleReplay(deadLetterId: string | undefined) {
  if (!deadLetterId) return

  try {
    await replay(deadLetterId)
    toast.success('死信消息已重放')
  } catch (error) {
    ignoreHandledError(error)
  }
}

async function handleReplayFiltered() {
  try {
    const result = await replayFiltered()
    toast.success(`已提交 ${result.items?.length ?? 0} 条匹配死信重放`)
  } catch (error) {
    ignoreHandledError(error)
  }
}

async function handleIgnore() {
  const deadLetterId = selectedDeadLetter.value?.id
  if (!deadLetterId || !ignoreReason.value.trim()) return

  try {
    await ignore(deadLetterId, ignoreReason.value)
    toast.success('死信消息已忽略')
    ignoreReason.value = ''
  } catch (error) {
    ignoreHandledError(error)
  }
}

function selectDeadLetter(item: NotificationDeadLetterResponse) {
  selectedDeadLetterId.value = item.id
}

function statusClass(status: string | null | undefined) {
  switch ((status ?? '').toLowerCase()) {
    case 'pending':
      return 'border-amber-200 bg-amber-50 text-amber-700'
    case 'failed':
      return 'border-red-200 bg-red-50 text-red-700'
    case 'replayed':
      return 'border-emerald-200 bg-emerald-50 text-emerald-700'
    case 'ignored':
      return 'border-slate-200 bg-slate-50 text-slate-700'
    default:
      return 'border-muted bg-muted text-muted-foreground'
  }
}

function formatDate(value: string | null | undefined) {
  if (!value) return '-'
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function shortText(value: string | null | undefined, fallback = '-') {
  if (!value) return fallback
  return value.length > 42 ? `${value.slice(0, 39)}...` : value
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="死信队列"
        :breadcrumbs="[{ label: '通知' }]"
        :count="`${deadLetters.length} 条`"
      >
        <template #actions>
          <Button
            :disabled="refreshPending"
            size="sm"
            type="button"
            variant="outline"
            aria-label="刷新死信队列"
            @click="handleRefresh"
          >
            <RefreshCwIcon class="size-4" aria-hidden="true" />
            刷新
          </Button>
          <Button
            :disabled="actionPending || deadLetters.length === 0"
            size="sm"
            type="button"
            aria-label="重放当前筛选死信"
            @click="handleReplayFiltered"
          >
            <RotateCwIcon class="size-4" aria-hidden="true" />
            批量重放
          </Button>
        </template>
      </NvPageHeader>

      <NvSectionCards :columns="4">
        <NvSectionCard description="当前筛选" :value="deadLetters.length" hint="列表结果" />
        <NvSectionCard description="可处理积压" :value="actionableCount" hint="Pending + Failed" />
        <NvSectionCard description="待重放" :value="pendingCount" hint="全局 Pending" />
        <NvSectionCard description="重放失败" :value="failedCount" hint="全局 Failed" />
      </NvSectionCards>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">
        无法更新死信队列：{{ errorMessage }}
      </p>

      <section class="grid gap-3 rounded-lg border bg-card p-4">
        <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_12rem]">
          <label class="grid gap-1 text-sm font-medium">
            <span>事件类型</span>
            <input
              v-model="eventTypeFilter"
              class="h-9 rounded-md border bg-background px-3 text-sm"
              placeholder="ops.OperationTaskFailed"
            />
          </label>
          <label class="grid gap-1 text-sm font-medium">
            <span>消费者</span>
            <input
              v-model="consumerNameFilter"
              class="h-9 rounded-md border bg-background px-3 text-sm"
              placeholder="notification.operation-task-failed"
            />
          </label>
          <label class="grid gap-1 text-sm font-medium">
            <span>状态</span>
            <select v-model="statusFilter" class="h-9 rounded-md border bg-background px-3 text-sm">
              <option value="">全部</option>
              <option value="Pending">Pending</option>
              <option value="Failed">Failed</option>
              <option value="Replayed">Replayed</option>
              <option value="Ignored">Ignored</option>
            </select>
          </label>
        </div>
      </section>

      <div class="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_28rem]">
        <section class="overflow-hidden rounded-lg border bg-card">
          <div class="overflow-x-auto">
            <table class="w-full min-w-[48rem] text-left text-sm">
              <thead class="border-b bg-muted/40 text-xs uppercase text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 font-medium">状态</th>
                  <th class="px-3 py-2 font-medium">事件类型</th>
                  <th class="px-3 py-2 font-medium">消费者</th>
                  <th class="px-3 py-2 font-medium">原因</th>
                  <th class="px-3 py-2 font-medium">进入时间</th>
                  <th class="px-3 py-2 font-medium text-right">操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="listPending">
                  <td class="px-3 py-6 text-center text-muted-foreground" colspan="6">正在加载</td>
                </tr>
                <tr v-else-if="deadLetters.length === 0">
                  <td class="px-3 py-6 text-center text-muted-foreground" colspan="6">
                    暂无匹配死信消息
                  </td>
                </tr>
                <template v-else>
                  <tr
                    v-for="item in deadLetters"
                    :key="item.id"
                    class="border-b last:border-b-0 hover:bg-muted/40"
                    :class="item.id === selectedDeadLetterId ? 'bg-muted/50' : ''"
                  >
                    <td class="px-3 py-3 align-top">
                      <button
                        class="rounded-full border px-2 py-0.5 text-xs font-medium"
                        :class="statusClass(item.status)"
                        type="button"
                        @click="selectDeadLetter(item)"
                      >
                        {{ item.status ?? '-' }}
                      </button>
                    </td>
                    <td class="px-3 py-3 align-top font-medium">
                      {{ shortText(item.eventType) }}
                    </td>
                    <td class="px-3 py-3 align-top text-muted-foreground">
                      {{ shortText(item.consumerName) }}
                    </td>
                    <td class="px-3 py-3 align-top text-muted-foreground">
                      {{ item.failureCode }}
                    </td>
                    <td class="px-3 py-3 align-top text-muted-foreground">
                      {{ formatDate(item.deadLetteredAtUtc) }}
                    </td>
                    <td class="px-3 py-3 align-top">
                      <div class="flex justify-end gap-2">
                        <Button
                          size="sm"
                          type="button"
                          variant="outline"
                          :aria-label="`查看死信：${item.eventId ?? item.id}`"
                          @click="selectDeadLetter(item)"
                        >
                          查看
                        </Button>
                        <Button
                          size="sm"
                          type="button"
                          :disabled="actionPending || item.status === 'Replayed'"
                          :aria-label="`重放死信：${item.eventId ?? item.id}`"
                          @click="handleReplay(item.id)"
                        >
                          <PlayIcon class="size-4" aria-hidden="true" />
                        </Button>
                      </div>
                    </td>
                  </tr>
                </template>
              </tbody>
            </table>
          </div>
        </section>

        <aside class="grid gap-4 rounded-lg border bg-card p-4">
          <header class="grid gap-1">
            <h2 class="text-base font-semibold">死信详情</h2>
            <p class="text-xs text-muted-foreground">
              {{ selectedDeadLetter?.eventId ?? selectedDeadLetterId ?? '未选择' }}
            </p>
          </header>

          <div v-if="detailPending" class="text-sm text-muted-foreground">正在加载详情</div>
          <div v-else-if="selectedDeadLetter" class="grid gap-3 text-sm">
            <dl class="grid grid-cols-[6rem_minmax(0,1fr)] gap-x-3 gap-y-2">
              <dt class="text-muted-foreground">状态</dt>
              <dd>{{ selectedDeadLetter.status }}</dd>
              <dt class="text-muted-foreground">事件类型</dt>
              <dd class="break-all">{{ selectedDeadLetter.eventType ?? '-' }}</dd>
              <dt class="text-muted-foreground">消费者</dt>
              <dd class="break-all">{{ selectedDeadLetter.consumerName }}</dd>
              <dt class="text-muted-foreground">来源</dt>
              <dd>{{ selectedDeadLetter.sourceService ?? '-' }}</dd>
              <dt class="text-muted-foreground">失败原因</dt>
              <dd class="break-words">{{ selectedDeadLetter.failureCode }}</dd>
              <dt class="text-muted-foreground">重放时间</dt>
              <dd>{{ formatDate(selectedDeadLetter.replayedAtUtc) }}</dd>
            </dl>

            <pre class="max-h-72 overflow-auto rounded-md bg-muted p-3 text-xs leading-relaxed">{{
              selectedDeadLetter.eventJson
            }}</pre>

            <div class="grid gap-2">
              <textarea
                v-model="ignoreReason"
                class="min-h-20 rounded-md border bg-background px-3 py-2 text-sm"
                placeholder="忽略原因"
              />
              <div class="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  :disabled="actionPending || !canReplaySelected"
                  aria-label="重放选中死信"
                  @click="handleReplay(selectedDeadLetter.id)"
                >
                  <PlayIcon class="size-4" aria-hidden="true" />
                  重放
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  :disabled="actionPending || !canIgnoreSelected"
                  aria-label="忽略选中死信"
                  @click="handleIgnore"
                >
                  <BanIcon class="size-4" aria-hidden="true" />
                  忽略
                </Button>
              </div>
            </div>
          </div>
          <p v-else class="text-sm text-muted-foreground">请选择一条死信消息。</p>
        </aside>
      </div>
    </section>
  </DefaultLayout>
</template>
