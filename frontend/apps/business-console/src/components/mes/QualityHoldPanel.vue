<script setup lang="ts">
import { useMesQualityHold } from '@/composables/useBusinessMes'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldLabel,
  NvInput,
  NvStatusBadge,
  Spinner,
} from '@nerv-iip/ui'
import { LinkIcon, LockIcon, RefreshCwIcon, UnlockIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

const props = defineProps<{
  organizationId: string
  environmentId: string
  sourceService: string
  sourceDocumentId: string
  scope?: string | null
  operationTaskId?: string | null
  holdReason?: string | null
  heldAtUtc?: string | null
  heldBy?: string | null
  canManage: boolean
}>()

const emit = defineEmits<{ released: [] }>()

const {
  timeline,
  timelinePending,
  timelineError,
  refreshTimeline,
  forceRelease,
  forceReleasePending,
} = useMesQualityHold(() => ({
  organizationId: props.organizationId,
  environmentId: props.environmentId,
  sourceService: props.sourceService,
  sourceDocumentId: props.sourceDocumentId,
}))

const releaseOpen = ref(false)
const releaseReason = ref('')

const scopeLabel = computed(() =>
  props.scope === 'operation-task' ? '工序级保留' : '工单级保留',
)

// 事件类型（EventKind）与释放方式（Origin）→ 一线可读文案。自动 = 检验联动，人工 = 强制释放。
const EVENT_KIND_LABELS: Record<string, string> = {
  'hold-applied': '施加质量保留',
  'inspection-released': '复检合格自动放行',
  'manual-force-released': '人工强制释放',
  'manual-force-release-noop': '人工释放（保留已失效）',
}
function eventKindLabel(kind?: string | null) {
  return EVENT_KIND_LABELS[kind ?? ''] ?? kind ?? '状态变化'
}
function originLabel(origin?: string | null) {
  if (origin === 'automatic') return '自动'
  if (origin === 'manual') return '人工'
  return origin ?? '—'
}
function isReleaseEvent(kind?: string | null) {
  return kind === 'inspection-released' || kind === 'manual-force-released'
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

async function confirmRelease() {
  const reason = releaseReason.value.trim()
  if (!reason || forceReleasePending.value) return
  try {
    await forceRelease(reason)
    releaseOpen.value = false
    releaseReason.value = ''
    notifySuccess(`已人工强制释放质量保留（${props.sourceDocumentId}）。`)
    void refreshTimeline()
    emit('released')
  } catch (error) {
    notifyError(error, '强制释放质量保留失败，请稍后重试。')
  }
}

function timelineErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : error ? '时间线加载失败，请稍后重试。' : ''
}
</script>

<template>
  <section class="grid gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-4">
    <header class="flex flex-wrap items-start justify-between gap-2">
      <div class="grid gap-1.5">
        <div class="flex items-center gap-2">
          <LockIcon class="size-4 text-destructive" aria-hidden="true" />
          <NvStatusBadge tone="danger" label="质量保留中" />
          <NvStatusBadge tone="neutral" :label="scopeLabel" />
        </div>
        <p class="text-sm text-foreground">
          <span class="text-muted-foreground">保留对象：</span>{{ sourceDocumentId }}
          <span v-if="operationTaskId" class="text-muted-foreground"
            >（工序 {{ operationTaskId }}）</span
          >
        </p>
        <p class="text-sm text-muted-foreground">
          由 {{ heldBy ?? '质量' }} 于 {{ formatDateTime(heldAtUtc) }} 施加<span v-if="holdReason"
            >：{{ holdReason }}</span
          >
        </p>
      </div>
      <div class="flex items-center gap-1">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="timelinePending"
          @click="refreshTimeline"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <!-- 人工强制释放：需 business.mes.quality.write，破坏性动作原因必填（A1 §2）。 -->
        <NvButton
          v-if="canManage"
          size="sm"
          type="button"
          variant="destructive"
          @click="releaseOpen = true"
        >
          <UnlockIcon aria-hidden="true" />
          强制释放
        </NvButton>
      </div>
    </header>

    <p v-if="timelineError" class="text-sm text-destructive" role="alert">
      {{ timelineErrorMessage(timelineError) }}
    </p>

    <div class="grid gap-2">
      <span class="text-xs font-semibold uppercase tracking-wide text-muted-foreground"
        >保留时间线</span
      >
      <div v-if="timelinePending" class="flex items-center gap-2 text-sm text-muted-foreground">
        <Spinner aria-hidden="true" />
        <span>正在加载时间线…</span>
      </div>
      <ol v-else-if="timeline.length" class="grid gap-2">
        <li
          v-for="item in timeline"
          :key="item.transitionId ?? `${item.eventKind}-${item.occurredAtUtc}`"
          class="grid gap-1 rounded-md border bg-background p-3"
        >
          <div class="flex flex-wrap items-center gap-2">
            <NvStatusBadge
              :tone="isReleaseEvent(item.eventKind) ? 'success' : 'warning'"
              :label="eventKindLabel(item.eventKind)"
            />
            <span class="text-xs text-muted-foreground">{{ originLabel(item.origin) }}</span>
            <span class="text-xs text-muted-foreground">·</span>
            <span class="text-xs text-muted-foreground">{{ item.actor ?? '—' }}</span>
            <span class="ml-auto text-xs tabular-nums text-muted-foreground">{{
              formatDateTime(item.occurredAtUtc)
            }}</span>
          </div>
          <p v-if="item.reason" class="text-sm text-muted-foreground">{{ item.reason }}</p>
          <!-- 施加来源检验单互链（A1 §5.3 跨页上下文 query）：跳检验任务与记录并按来源单号定位。 -->
          <RouterLink
            v-if="item.sourceInspectionRecordId || item.sourceInspectionDocumentId"
            :to="{
              path: '/quality/inspections',
              query: {
                keyword: item.sourceInspectionDocumentId ?? item.sourceInspectionRecordId,
              },
            }"
            class="inline-flex w-fit items-center gap-1 text-xs text-brand underline-offset-4 hover:underline"
          >
            <LinkIcon class="size-3" aria-hidden="true" />
            来源检验单 {{ item.sourceInspectionDocumentId ?? item.sourceInspectionRecordId }}
          </RouterLink>
        </li>
      </ol>
      <p v-else class="text-sm text-muted-foreground">暂无保留事件记录。</p>
    </div>

    <NvAlertDialog v-model:open="releaseOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>人工强制释放质量保留</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            强制释放会解除该对象的质量保留、允许继续放行或开工，并记入保留时间线。请填写释放理由，随请求提交并进入审计。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvField>
          <NvFieldLabel for="force-release-reason">
            释放理由 <span class="text-destructive">*</span>
          </NvFieldLabel>
          <NvInput
            id="force-release-reason"
            v-model="releaseReason"
            :maxlength="500"
            placeholder="说明为何强制释放该质量保留"
          />
        </NvField>
        <NvAlertDialogFooter>
          <NvButton
            type="button"
            variant="outline"
            :disabled="forceReleasePending"
            @click="releaseOpen = false"
          >
            取消
          </NvButton>
          <NvButton
            type="button"
            variant="destructive"
            :disabled="!releaseReason.trim() || forceReleasePending"
            @click="confirmRelease"
          >
            <Spinner v-if="forceReleasePending" aria-hidden="true" />
            <UnlockIcon v-else aria-hidden="true" />
            确认强制释放
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </section>
</template>
