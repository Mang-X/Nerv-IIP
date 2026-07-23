<script setup lang="ts">
import type {
  BusinessConsoleOrderUrgency,
  BusinessConsoleOrderUrgencyBusinessPriority,
} from '@nerv-iip/api-client'
import type { UrgencyDisplayMode } from '@/composables/useUrgencyDisplayMode'
import { formatUrgencyDisplay, urgencyLevelPresentation } from '@/composables/useUrgencyDisplayMode'
import {
  useOrderUrgencyDetail,
  useSetOrderUrgencyBusinessPriority,
} from '@/composables/useOrderUrgencyDetail'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  NvButton,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

const props = withDefaults(
  defineProps<{
    orderReference: string
    urgency?: BusinessConsoleOrderUrgency
    mode?: UrgencyDisplayMode
  }>(),
  { mode: 'level' },
)

const emit = defineEmits<{ refresh: [] }>()

const open = shallowRef(false)
const auth = useAuthStore()
const canManagePriority = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.schedulingPlansManage),
)

const orderReferenceForDetail = computed(
  () => props.urgency?.orderId?.trim() || props.orderReference.trim(),
)
const { detail, pending: detailPending } = useOrderUrgencyDetail(orderReferenceForDetail, {
  enabled: open,
})
const {
  error: mutationError,
  pending: mutationPending,
  setBusinessPriority,
} = useSetOrderUrgencyBusinessPriority()

// Explanation reads the list-level urgency by default; once the detail loads (or a
// priority is written) the authoritative current revision takes over so the new
// revision shows immediately without waiting for the parent list to refetch.
const currentUrgency = computed<BusinessConsoleOrderUrgency | undefined>(
  () => detail.value?.current ?? props.urgency,
)
const currentPriority = computed<BusinessConsoleOrderUrgencyBusinessPriority | undefined>(
  () => currentUrgency.value?.businessPriority,
)
const priorityChanges = computed(() =>
  [...(detail.value?.businessPriorityChanges ?? [])].sort(
    (a, b) => (b.revision ?? 0) - (a.revision ?? 0),
  ),
)
const currentSetter = computed(() => priorityChanges.value[0]?.changedBy)

const presentation = computed(() => urgencyLevelPresentation(props.urgency?.level))
const badge = computed(() => formatUrgencyDisplay(props.urgency, props.mode))

const priorityLevelOptions = [
  { value: 'p0', label: 'P0 · 最高' },
  { value: 'p1', label: 'P1 · 高' },
  { value: 'p2', label: 'P2 · 标准' },
  { value: 'p3', label: 'P3 · 低' },
]
const form = reactive({ level: 'p1', reason: '', expiresAt: '' })
const formError = shallowRef('')

const schedulingRoute = computed(() => ({
  path: '/scheduling',
  query: { orderReference: orderReferenceForDetail.value },
}))

async function submitPriority() {
  formError.value = ''
  if (!form.reason.trim()) {
    formError.value = '请填写调整原因。'
    return
  }
  try {
    await setBusinessPriority({
      orderReference: orderReferenceForDetail.value,
      level: form.level,
      reason: form.reason.trim(),
      expiresAtUtc: toIsoOrNull(form.expiresAt),
    })
    form.reason = ''
    form.expiresAt = ''
    // Refresh the shared list/detail so the new revision propagates everywhere.
    emit('refresh')
  } catch {
    formError.value = formatError(mutationError.value) || '优先级调整失败，请稍后重试。'
  }
}

function toIsoOrNull(value: string): string | null {
  const trimmed = value.trim()
  if (!trimmed) return null
  const date = new Date(trimmed)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

function reasonLabel(code: string) {
  const labels: Record<string, string> = {
    'business.priority.p0': '业务优先级 P0',
    'business.priority.p1': '业务优先级 P1',
    'business.priority.p2': '业务优先级 P2',
    'business.priority.p3': '业务优先级 P3',
    'business.priority.expired': '人工优先级已过期',
    'time.due.overdue': '承诺时间已逾期',
    'time.due.missing': '缺少承诺时间',
    'time.slack.negative': 'Slack 为负',
    'time.slack.withinShift': 'Slack 小于一个班次',
    'time.cr.belowOne': '关键比率 CR 小于 1',
    'time.cr.attention': '关键比率 CR 接近 1',
    'material.shortage': '物料短缺',
    'equipment.unavailable': '设备不可用',
    'quality.hold': '质量阻断',
    'tooling.unavailable': '工装不可用',
    'capacity.insufficient': '产能不足',
    'urgency.source.missing': '权威事实缺失',
    'urgency.source.stale': '权威事实已过期',
    'execution.risk.none': '未发现执行风险',
  }
  return labels[code] ?? code
}

function priorityLabel(level?: string | null) {
  return level ? level.toUpperCase() : '—'
}

function formatNumber(value?: number | null) {
  return value == null ? '—' : String(value)
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('zh-CN', { hour12: false })
}

function hasTimeReason(code: string) {
  return props.urgency?.timeCriticality?.reasonCodes?.includes(code) ?? false
}
</script>

<template>
  <NvTooltipProvider>
    <NvTooltip>
      <NvTooltipTrigger as-child>
        <NvButton
          type="button"
          size="sm"
          variant="ghost"
          class="h-auto px-1"
          :disabled="!urgency"
          :aria-label="`查看 ${orderReference} 紧急度解释`"
          @click="open = true"
        >
          <NvStatusBadge :label="badge.label" :tone="badge.tone" />
        </NvButton>
      </NvTooltipTrigger>
      <NvTooltipContent v-if="urgency" class="grid max-w-sm gap-1 text-xs">
        <p class="font-medium">综合分级：{{ presentation.label }}</p>
        <p>业务优先级：{{ priorityLabel(urgency.businessPriority?.level) }}</p>
        <p>
          CR：{{ formatNumber(urgency.timeCriticality?.criticalRatio) }} · Slack：{{
            formatNumber(urgency.timeCriticality?.slackHours)
          }}
          h
        </p>
        <p>更新时间：{{ formatDateTime(urgency.calculatedAtUtc) }}</p>
        <p class="text-muted-foreground">点击查看完整判定依据</p>
      </NvTooltipContent>
    </NvTooltip>
  </NvTooltipProvider>

  <NvSheet v-model:open="open">
    <NvSheetContent side="right" class="w-full overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>{{ orderReference }} · 紧急度解释</NvSheetTitle>
        <NvSheetDescription>
          三类贡献项独立呈现；最终等级取最高风险，不合并为不可解释分数。
        </NvSheetDescription>
      </NvSheetHeader>

      <div v-if="urgency" class="mt-6 grid gap-4">
        <section class="rounded-lg border bg-background p-4">
          <div class="flex items-center justify-between gap-3">
            <h3 class="font-semibold text-foreground">统一结论</h3>
            <NvStatusBadge :label="presentation.label" :tone="presentation.tone" />
          </div>
          <dl class="mt-3 grid gap-2 text-sm text-muted-foreground">
            <div class="flex justify-between gap-4">
              <dt>模型版本</dt>
              <dd class="font-mono text-foreground">{{ urgency.modelVersion }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt>计算时间</dt>
              <dd class="text-foreground">{{ formatDateTime(urgency.calculatedAtUtc) }}</dd>
            </div>
          </dl>
        </section>

        <section class="rounded-lg border bg-background p-4">
          <div class="flex items-center justify-between gap-3">
            <h3 class="font-semibold text-foreground">业务优先级</h3>
            <NvStatusBadge :label="priorityLabel(currentPriority?.level)" tone="info" />
          </div>
          <dl class="mt-3 grid gap-2 text-sm text-muted-foreground">
            <div class="flex justify-between gap-4">
              <dt>当前设置</dt>
              <dd class="text-foreground">{{ currentPriority?.reason ?? '默认优先级' }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt>来源 / 修订</dt>
              <dd class="text-foreground">
                {{ currentPriority?.source ?? 'default' }} · rev
                {{ currentPriority?.revision ?? 0 }}
              </dd>
            </div>
            <div v-if="currentSetter" class="flex justify-between gap-4">
              <dt>设置人</dt>
              <dd class="text-foreground">{{ currentSetter }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt>生效窗口</dt>
              <dd class="text-foreground">
                {{ formatDateTime(currentPriority?.setAtUtc) }} →
                {{
                  currentPriority?.expiresAtUtc
                    ? formatDateTime(currentPriority.expiresAtUtc)
                    : '长期有效'
                }}
              </dd>
            </div>
          </dl>

          <form
            v-if="canManagePriority"
            class="mt-4 grid gap-3 border-t pt-4"
            data-testid="priority-editor"
            @submit.prevent="submitPriority"
          >
            <p class="text-sm font-medium text-foreground">设置人工优先级</p>
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel for="urgency-priority-level">优先级</NvFieldLabel>
                <NvSelect v-model="form.level">
                  <NvSelectTrigger id="urgency-priority-level" aria-label="人工优先级等级">
                    <NvSelectValue />
                  </NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="option in priorityLevelOptions"
                      :key="option.value"
                      :value="option.value"
                    >
                      {{ option.label }}
                    </NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="urgency-priority-expiry">有效期（可选）</NvFieldLabel>
                <NvInput
                  id="urgency-priority-expiry"
                  v-model="form.expiresAt"
                  type="datetime-local"
                />
              </NvField>
            </NvFieldGroup>
            <NvField>
              <NvFieldLabel for="urgency-priority-reason">
                调整原因 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="urgency-priority-reason"
                v-model="form.reason"
                autocomplete="off"
                placeholder="例如：重点客户插单，需优先保障"
              />
            </NvField>
            <NvFieldError v-if="formError" :errors="[formError]" />
            <div class="flex justify-end">
              <NvButton type="submit" size="sm" :disabled="mutationPending">保存优先级</NvButton>
            </div>
          </form>
          <p v-else class="mt-4 border-t pt-4 text-xs text-muted-foreground">
            仅排产管理权限可调整人工优先级；当前账号为只读。
          </p>

          <div class="mt-4 border-t pt-4">
            <h4 class="text-sm font-medium text-foreground">优先级审计历史</h4>
            <p v-if="detailPending" class="mt-2 text-xs text-muted-foreground">正在读取审计历史…</p>
            <div v-else-if="priorityChanges.length" class="mt-3 overflow-x-auto">
              <table class="w-full text-left text-xs">
                <thead class="text-muted-foreground">
                  <tr>
                    <th class="pb-2 pr-3 font-medium">时间</th>
                    <th class="pb-2 pr-3 font-medium">操作人</th>
                    <th class="pb-2 pr-3 font-medium">变更</th>
                    <th class="pb-2 pr-3 font-medium">原因</th>
                    <th class="pb-2 font-medium">有效期</th>
                  </tr>
                </thead>
                <tbody class="text-foreground">
                  <tr
                    v-for="change in priorityChanges"
                    :key="change.revision ?? change.changedAtUtc"
                    class="border-t"
                    data-testid="priority-audit-row"
                  >
                    <td class="py-2 pr-3">{{ formatDateTime(change.changedAtUtc) }}</td>
                    <td class="py-2 pr-3">{{ change.changedBy ?? '—' }}</td>
                    <td class="py-2 pr-3">
                      {{ priorityLabel(change.previousLevel) }} →
                      {{ priorityLabel(change.newLevel) }}
                    </td>
                    <td class="py-2 pr-3">{{ change.reason ?? '—' }}</td>
                    <td class="py-2">
                      {{ change.expiresAtUtc ? formatDateTime(change.expiresAtUtc) : '长期有效' }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="mt-2 text-xs text-muted-foreground">暂无人工优先级调整记录。</p>
          </div>

          <ul class="mt-3 grid gap-1 text-sm text-muted-foreground">
            <li v-for="code in urgency.businessPriority?.reasonCodes ?? []" :key="code">
              {{ reasonLabel(code) }} <span class="font-mono text-xs">({{ code }})</span>
            </li>
          </ul>
        </section>

        <section class="rounded-lg border bg-background p-4">
          <h3 class="font-semibold text-foreground">CR / Slack 时间紧迫度</h3>
          <dl class="mt-3 grid grid-cols-3 gap-3 text-sm">
            <div>
              <dt class="text-muted-foreground">CR</dt>
              <dd class="font-medium text-foreground">
                {{ formatNumber(urgency.timeCriticality?.criticalRatio) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted-foreground">Slack</dt>
              <dd class="font-medium text-foreground">
                {{ formatNumber(urgency.timeCriticality?.slackHours) }} h
              </dd>
            </div>
            <div>
              <dt class="text-muted-foreground">预计延误</dt>
              <dd class="font-medium text-foreground">
                {{ formatNumber(urgency.timeCriticality?.expectedDelayHours) }} h
              </dd>
            </div>
          </dl>
          <div class="mt-4 overflow-x-auto">
            <table class="w-full text-left text-xs">
              <thead class="text-muted-foreground">
                <tr>
                  <th class="pb-2 pr-3 font-medium">判定项</th>
                  <th class="pb-2 pr-3 font-medium">当前值</th>
                  <th class="pb-2 pr-3 font-medium">阈值</th>
                  <th class="pb-2 font-medium">结果</th>
                </tr>
              </thead>
              <tbody class="text-foreground">
                <tr class="border-t">
                  <td class="py-2 pr-3">CR</td>
                  <td class="py-2 pr-3">
                    {{ formatNumber(urgency.timeCriticality?.criticalRatio) }}
                  </td>
                  <td class="py-2 pr-3">&lt; 1 紧急；≤ 1.2 关注</td>
                  <td class="py-2">
                    {{
                      hasTimeReason('time.cr.belowOne')
                        ? '紧急'
                        : hasTimeReason('time.cr.attention')
                          ? '关注'
                          : '未触发'
                    }}
                  </td>
                </tr>
                <tr class="border-t">
                  <td class="py-2 pr-3">Slack</td>
                  <td class="py-2 pr-3">
                    {{ formatNumber(urgency.timeCriticality?.slackHours) }} h
                  </td>
                  <td class="py-2 pr-3">&lt; 0 紧急；&lt; 8 h 高风险</td>
                  <td class="py-2">
                    {{
                      hasTimeReason('time.slack.negative')
                        ? '紧急'
                        : hasTimeReason('time.slack.withinShift')
                          ? '高风险'
                          : '未触发'
                    }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <ul class="mt-3 grid gap-1 text-sm text-muted-foreground">
            <li v-for="code in urgency.timeCriticality?.reasonCodes ?? []" :key="code">
              {{ reasonLabel(code) }} <span class="font-mono text-xs">({{ code }})</span>
            </li>
          </ul>
        </section>

        <section class="rounded-lg border bg-background p-4">
          <div class="flex items-center justify-between gap-3">
            <h3 class="font-semibold text-foreground">执行风险</h3>
            <NvStatusBadge
              v-if="urgency.executionRisk?.isSourceStale"
              label="事实过期"
              tone="warning"
            />
          </div>
          <ul class="mt-3 grid gap-1 text-sm text-muted-foreground">
            <li v-for="code in urgency.executionRisk?.reasonCodes ?? []" :key="code">
              {{ reasonLabel(code) }} <span class="font-mono text-xs">({{ code }})</span>
            </li>
          </ul>
        </section>

        <NvButton as-child class="w-full">
          <RouterLink :to="schedulingRoute">进入排产调整</RouterLink>
        </NvButton>
      </div>
    </NvSheetContent>
  </NvSheet>
</template>
