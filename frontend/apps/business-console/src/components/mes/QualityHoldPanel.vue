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
import { LinkIcon, LockIcon, RefreshCwIcon, UnlockIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'

const props = defineProps<{
  organizationId: string
  environmentId: string
  sourceService: string
  sourceDocumentId: string
  scope?: string | null
  isActive?: boolean
  operationTaskId?: string | null
  holdReason?: string | null
  heldAtUtc?: string | null
  heldBy?: string | null
  releasedAtUtc?: string | null
  releasedBy?: string | null
  releaseReason?: string | null
  releaseSource?: string | null
  canManage: boolean
  // 时间线读端点要求 business.mes.quality.read；无该权限则不加载时间线（避免逐个保留 403）。
  canReadTimeline: boolean
}>()

const emit = defineEmits<{ released: [] }>()

const {
  timeline,
  timelinePending,
  timelineError,
  refreshTimeline,
  forceRelease,
  forceReleasePending,
} = useMesQualityHold(
  () => ({
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    sourceService: props.sourceService,
    sourceDocumentId: props.sourceDocumentId,
  }),
  () => props.canReadTimeline,
)

const releaseOpen = ref(false)
const releaseReason = ref('')
// 点提交才标红（create-dialog 硬规则）：必填理由为空时不禁用按钮，而是标红 + 提示且不发请求。
const releaseShowErrors = ref(false)
watch(releaseOpen, (open) => {
  if (!open) {
    releaseReason.value = ''
    releaseShowErrors.value = false
  }
})

// 时间线加载失败（网络/5xx）以 toast 告知，页面内不保留常驻红条（业务前端反馈约束）；
// 空态仍有「刷新」可重试。
watch(timelineError, (err) => {
  if (err) notifyError(err, '质量保留时间线加载失败，请稍后重试。')
})

const scopeLabel = computed(() => (props.scope === 'operation-task' ? '工序级保留' : '工单级保留'))

// 释放方式（ReleaseSource）→ 一线可读：manual-force-release=人工强制释放，其余（检验联动）=复检自动放行。
const releaseSourceLabel = computed(() =>
  props.releaseSource === 'manual-force-release' ? '人工强制释放' : '复检自动放行',
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
  // 点提交才校验：理由为空则标红 + 提示，不发请求。
  releaseShowErrors.value = true
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
</script>

<template>
  <section
    class="grid gap-3 rounded-lg border p-4"
    :class="isActive ? 'border-destructive/30 bg-destructive/5' : 'border-border bg-muted/30'"
  >
    <header class="flex flex-wrap items-start justify-between gap-2">
      <div class="grid gap-1.5">
        <div class="flex items-center gap-2">
          <LockIcon v-if="isActive" class="size-4 text-destructive" aria-hidden="true" />
          <NvStatusBadge
            :tone="isActive ? 'danger' : 'success'"
            :label="isActive ? '质量保留中' : '已释放'"
          />
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
        <!-- 已释放周期：释放时间/方式/理由在此常驻可见（满足「释放后时间线完整」的验收）。 -->
        <p v-if="!isActive && releasedAtUtc" class="text-sm text-success">
          由 {{ releasedBy ?? '—' }} 于 {{ formatDateTime(releasedAtUtc) }} {{ releaseSourceLabel
          }}<span v-if="releaseReason">：{{ releaseReason }}</span>
        </p>
      </div>
      <div class="flex items-center gap-1">
        <NvButton
          v-if="canReadTimeline"
          size="sm"
          type="button"
          variant="outline"
          :disabled="timelinePending"
          @click="refreshTimeline"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <!-- 人工强制释放：仅活跃保留可发起，需 business.mes.quality.write，破坏性动作原因必填（A1 §2）。 -->
        <NvButton
          v-if="isActive && canManage"
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

    <div class="grid gap-2">
      <span class="text-xs font-semibold uppercase tracking-wide text-muted-foreground"
        >保留时间线</span
      >
      <!-- 时间线读端点需 business.mes.quality.read；无该权限则不请求（否则逐个保留 403），给出明确说明。 -->
      <p v-if="!canReadTimeline" class="text-sm text-muted-foreground">
        需要「质量」读取权限才能查看保留时间线。
      </p>
      <div
        v-else-if="timelinePending"
        class="flex items-center gap-2 text-sm text-muted-foreground"
      >
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
          <!-- 来源检验互链（A1 §5.3 跨页上下文 query）：记录 id 恒在→带 inspectionRecordId 直接定位到只读
               检验记录详情；方案 id 可空,存在时并带 inspectionPlanId 供列表方案上下文。覆盖无方案但有记录的保留。 -->
          <RouterLink
            v-if="item.sourceInspectionRecordId || item.sourceInspectionDocumentId"
            :to="{
              path: '/quality/inspections',
              query: {
                ...(item.sourceInspectionRecordId
                  ? { inspectionRecordId: item.sourceInspectionRecordId }
                  : {}),
                ...(item.sourceInspectionDocumentId
                  ? { inspectionPlanId: item.sourceInspectionDocumentId }
                  : {}),
              },
            }"
            class="inline-flex w-fit items-center gap-1 text-xs text-brand underline-offset-4 hover:underline"
          >
            <LinkIcon class="size-3" aria-hidden="true" />
            <span v-if="item.sourceInspectionRecordId"
              >来源检验记录 {{ item.sourceInspectionRecordId }}</span
            >
            <span v-else>来源检验方案 {{ item.sourceInspectionDocumentId }}</span>
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
            :data-invalid="releaseShowErrors && !releaseReason.trim() ? '' : undefined"
          />
          <p
            v-if="releaseShowErrors && !releaseReason.trim()"
            class="text-xs text-destructive"
            role="alert"
          >
            请填写释放理由（已标红）。
          </p>
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
            :disabled="forceReleasePending"
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
