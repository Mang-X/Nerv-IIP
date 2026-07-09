<script setup lang="ts">
import type { OperationTaskResponse } from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'
import { Card, CardContent, CardHeader, CardTitle, Separator, NvStatusBadge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  operationTask?: OperationTaskResponse
  pending?: boolean
}>()

type AuditRecord = NonNullable<OperationTaskResponse['auditRecords']>[number]
type OperationAttempt = NonNullable<OperationTaskResponse['attempts']>[number]

const auditRecords = computed(() => props.operationTask?.auditRecords ?? [])
const attempts = computed(() => props.operationTask?.attempts ?? [])

const STATUS_LABELS: Record<string, string> = {
  completed: '已完成',
  success: '成功',
  succeeded: '成功',
  failed: '失败',
  failure: '失败',
  cancelled: '已取消',
  canceled: '已取消',
  pending: '待处理',
  queued: '排队中',
  running: '执行中',
  loading: '加载中',
  unknown: '未知',
}

function statusTone(status?: string | null): StatusTone {
  const s = status?.toLowerCase()
  if (s === 'failed' || s === 'cancelled' || s === 'canceled' || s === 'failure') return 'danger'
  if (s === 'completed' || s === 'success' || s === 'succeeded') return 'success'
  return 'neutral'
}

function statusLabel(status?: string | null) {
  if (!status) return props.pending ? '加载中' : '未知'
  return STATUS_LABELS[status.toLowerCase()] ?? status
}

function attemptKey(attempt: OperationAttempt, index: number) {
  return (
    attempt.attemptId ?? `attempt:${attempt.startedAtUtc ?? attempt.status ?? 'unknown'}:${index}`
  )
}

function auditRecordKey(record: AuditRecord, index: number) {
  return (
    record.auditRecordId ?? `audit:${record.occurredAtUtc ?? record.action ?? 'unknown'}:${index}`
  )
}
</script>

<template>
  <Card aria-labelledby="operation-timeline-title">
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between gap-4">
        <div class="flex flex-col gap-0.5">
          <p class="text-xs font-bold uppercase tracking-wider text-primary">运维操作</p>
          <CardTitle id="operation-timeline-title" class="text-xl">
            {{ operationTask?.operationCode ?? '任务' }}
          </CardTitle>
        </div>
        <NvStatusBadge
          :label="statusLabel(operationTask?.status)"
          :tone="statusTone(operationTask?.status)"
        />
      </div>
    </CardHeader>

    <Separator />

    <CardContent class="pt-4">
      <p v-if="pending && !operationTask" class="text-sm text-muted-foreground">
        正在加载运维任务…
      </p>

      <template v-else-if="operationTask">
        <dl class="grid grid-cols-2 gap-3 sm:grid-cols-4 m-0">
          <div
            v-for="[label, value] in [
              ['任务 ID', operationTask.operationTaskId ?? '未知'],
              ['实例', operationTask.instanceKey ?? '未知'],
              ['申请人', operationTask.requestedBy ?? '未知'],
              ['申请时间', operationTask.requestedAtUtc ?? '未知'],
            ]"
            :key="label"
            class="flex flex-col gap-0.5 rounded-md border bg-muted/40 p-3"
          >
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">
              {{ label }}
            </dt>
            <dd class="break-anywhere text-sm m-0">{{ value }}</dd>
          </div>
        </dl>

        <Separator class="my-4" />

        <section aria-labelledby="attempts-title">
          <h2 id="attempts-title" class="mb-3 text-base font-semibold">执行尝试</h2>
          <ol v-if="attempts.length" class="flex flex-col gap-3 list-none m-0 p-0">
            <li
              v-for="(attempt, index) in attempts"
              :key="attemptKey(attempt, index)"
              class="flex flex-col gap-1 border-l-2 border-primary pl-3"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <strong class="text-sm">{{ attempt.attemptId ?? '尝试' }}</strong>
                <NvStatusBadge
                  :label="statusLabel(attempt.status)"
                  :tone="statusTone(attempt.status)"
                />
              </div>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ attempt.startedAtUtc ?? '无开始时间' }}
              </span>
              <span
                v-if="attempt.finishedAtUtc"
                class="text-xs text-muted-foreground break-anywhere"
              >
                {{ attempt.finishedAtUtc }}
              </span>
              <span
                v-if="attempt.failureCode"
                class="text-xs font-semibold text-destructive break-anywhere"
              >
                {{ attempt.failureCode }}
              </span>
            </li>
          </ol>
          <p v-else class="text-sm text-muted-foreground">暂无执行尝试。</p>
        </section>

        <Separator class="my-4" />

        <section aria-labelledby="audit-title">
          <h2 id="audit-title" class="mb-3 text-base font-semibold">审计记录</h2>
          <ol v-if="auditRecords.length" class="flex flex-col gap-3 list-none m-0 p-0">
            <li
              v-for="(record, index) in auditRecords"
              :key="auditRecordKey(record, index)"
              class="flex flex-col gap-1 border-l-2 border-primary pl-3"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <strong class="text-sm">{{ record.action ?? '审计事件' }}</strong>
                <span class="text-sm">{{ record.actor ?? '未知操作者' }}</span>
              </div>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ record.occurredAtUtc ?? '无时间戳' }}
              </span>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ record.correlationId ?? '无关联 ID' }}
              </span>
            </li>
          </ol>
          <p v-else class="text-sm text-muted-foreground">暂无审计记录。</p>
        </section>
      </template>

      <p v-else class="text-sm text-muted-foreground">未找到该运维任务。</p>
    </CardContent>
  </Card>
</template>
