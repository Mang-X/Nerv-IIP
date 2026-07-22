<script setup lang="ts">
import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'
import {
  NvButton,
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
import { computed, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  orderReference: string
  urgency?: BusinessConsoleOrderUrgency
}>()

const open = shallowRef(false)
const presentation = computed(() => urgencyPresentation(props.urgency?.level))
const primaryReason = computed(() => {
  const code = props.urgency?.executionRisk?.reasonCodes?.find(
    (value) => !value.startsWith('urgency.source.'),
  )
  return code ? reasonLabel(code) : undefined
})
const summaryLabel = computed(() => {
  const cr = props.urgency?.timeCriticality?.criticalRatio
  if (cr != null) return `${presentation.value.label} · CR ${cr}`
  return primaryReason.value
    ? `${presentation.value.label} · ${primaryReason.value}`
    : presentation.value.label
})
const schedulingRoute = computed(() => ({
  path: '/scheduling',
  query: { orderReference: props.urgency?.orderId?.trim() || props.orderReference.trim() },
}))

function urgencyPresentation(level?: string | null): { label: string; tone: StatusTone } {
  switch ((level ?? '').toLowerCase()) {
    case 'critical':
      return { label: '特急', tone: 'danger' }
    case 'urgent':
      return { label: '紧急', tone: 'danger' }
    case 'highrisk':
      return { label: '高风险', tone: 'warning' }
    case 'attention':
      return { label: '关注', tone: 'warning' }
    case 'normal':
      return { label: '正常', tone: 'success' }
    default:
      return { label: '未计算', tone: 'neutral' }
  }
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
          <NvStatusBadge :label="summaryLabel" :tone="presentation.tone" />
        </NvButton>
      </NvTooltipTrigger>
      <NvTooltipContent v-if="urgency" class="grid max-w-sm gap-1 text-xs">
        <p class="font-medium">综合分级：{{ presentation.label }}</p>
        <p>业务优先级：{{ urgency.businessPriority?.level?.toUpperCase() ?? 'P2' }}</p>
        <p>
          CR：{{ formatNumber(urgency.timeCriticality?.criticalRatio) }} · Slack：{{
            formatNumber(urgency.timeCriticality?.slackHours)
          }}
          h
        </p>
        <p>主要风险：{{ primaryReason ?? '未发现执行阻塞' }}</p>
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
          <h3 class="font-semibold text-foreground">业务优先级</h3>
          <p class="mt-2 text-sm text-foreground">
            {{ urgency.businessPriority?.level?.toUpperCase() ?? 'P2' }} ·
            {{ urgency.businessPriority?.reason ?? '默认优先级' }}
          </p>
          <p class="mt-1 text-xs text-muted-foreground">
            修订 {{ urgency.businessPriority?.revision ?? 0 }} · 来源
            {{ urgency.businessPriority?.source ?? 'default' }}
          </p>
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
