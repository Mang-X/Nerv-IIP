<script setup lang="ts">
import { Badge, Card, CardContent, CardHeader, CardTitle, Separator } from '@nerv-iip/ui'
import type { OperationTaskResponse } from '@nerv-iip/api-client'
import { computed } from 'vue'

const props = defineProps<{
  operationTask?: OperationTaskResponse
  pending?: boolean
}>()

type AuditRecord = NonNullable<OperationTaskResponse['auditRecords']>[number]
type OperationAttempt = NonNullable<OperationTaskResponse['attempts']>[number]

const auditRecords = computed(() => props.operationTask?.auditRecords ?? [])
const attempts = computed(() => props.operationTask?.attempts ?? [])

function badgeVariant(status?: string | null) {
  const s = status?.toLowerCase()
  return s === 'failed' || s === 'cancelled' || s === 'canceled' || s === 'failure'
    ? 'destructive'
    : s === 'completed' || s === 'success'
      ? 'success'
      : 'secondary'
}

function attemptKey(attempt: OperationAttempt, index: number) {
  return attempt.attemptId ?? `attempt:${attempt.startedAtUtc ?? attempt.status ?? 'unknown'}:${index}`
}

function auditRecordKey(record: AuditRecord, index: number) {
  return record.auditRecordId ?? `audit:${record.occurredAtUtc ?? record.action ?? 'unknown'}:${index}`
}
</script>

<template>
  <Card aria-labelledby="operation-timeline-title">
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between gap-4">
        <div class="flex flex-col gap-0.5">
          <p class="text-xs font-bold uppercase tracking-wider text-primary">Operation</p>
          <CardTitle id="operation-timeline-title" class="text-xl">
            {{ operationTask?.operationCode ?? 'Task' }}
          </CardTitle>
        </div>
        <Badge :variant="badgeVariant(operationTask?.status)">
          {{ operationTask?.status ?? (pending ? 'loading' : 'unknown') }}
        </Badge>
      </div>
    </CardHeader>

    <Separator />

    <CardContent class="pt-4">
      <p v-if="pending && !operationTask" class="text-sm text-muted-foreground">
        Loading operation task...
      </p>

      <template v-else-if="operationTask">
        <dl class="grid grid-cols-2 gap-3 sm:grid-cols-4 m-0">
          <div
            v-for="[label, value] in [
              ['Task ID', operationTask.operationTaskId ?? 'Unknown'],
              ['Instance', operationTask.instanceKey ?? 'Unknown'],
              ['Requested by', operationTask.requestedBy ?? 'Unknown'],
              ['Requested at', operationTask.requestedAtUtc ?? 'Unknown'],
            ]"
            :key="label"
            class="flex flex-col gap-0.5 rounded-md border bg-muted/40 p-3"
          >
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ label }}</dt>
            <dd class="break-anywhere text-sm m-0">{{ value }}</dd>
          </div>
        </dl>

        <Separator class="my-4" />

        <section aria-labelledby="attempts-title">
          <h2 id="attempts-title" class="mb-3 text-base font-semibold">Attempts</h2>
          <ol v-if="attempts.length" class="flex flex-col gap-3 list-none m-0 p-0">
            <li
              v-for="(attempt, index) in attempts"
              :key="attemptKey(attempt, index)"
              class="flex flex-col gap-1 border-l-2 border-primary pl-3"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <strong class="text-sm">{{ attempt.attemptId ?? 'Attempt' }}</strong>
                <Badge :variant="badgeVariant(attempt.status)">
                  {{ attempt.status ?? 'unknown' }}
                </Badge>
              </div>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ attempt.startedAtUtc ?? 'No start time' }}
              </span>
              <span v-if="attempt.finishedAtUtc" class="text-xs text-muted-foreground break-anywhere">
                {{ attempt.finishedAtUtc }}
              </span>
              <span v-if="attempt.failureCode" class="text-xs font-semibold text-destructive break-anywhere">
                {{ attempt.failureCode }}
              </span>
            </li>
          </ol>
          <p v-else class="text-sm text-muted-foreground">No attempts reported yet.</p>
        </section>

        <Separator class="my-4" />

        <section aria-labelledby="audit-title">
          <h2 id="audit-title" class="mb-3 text-base font-semibold">Audit Records</h2>
          <ol v-if="auditRecords.length" class="flex flex-col gap-3 list-none m-0 p-0">
            <li
              v-for="(record, index) in auditRecords"
              :key="auditRecordKey(record, index)"
              class="flex flex-col gap-1 border-l-2 border-primary pl-3"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <strong class="text-sm">{{ record.action ?? 'Audit event' }}</strong>
                <span class="text-sm">{{ record.actor ?? 'Unknown actor' }}</span>
              </div>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ record.occurredAtUtc ?? 'No timestamp' }}
              </span>
              <span class="text-xs text-muted-foreground break-anywhere">
                {{ record.correlationId ?? 'No correlation ID' }}
              </span>
            </li>
          </ol>
          <p v-else class="text-sm text-muted-foreground">No audit records reported yet.</p>
        </section>
      </template>

      <p v-else class="text-sm text-muted-foreground">Operation task was not found.</p>
    </CardContent>
  </Card>
</template>
