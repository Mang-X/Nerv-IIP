<script setup lang="ts">
import { UiBadge } from '@nerv-iip/ui'
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

function badgeTone(status?: string | null) {
  const normalized = status?.toLowerCase()

  if (normalized === 'completed' || normalized === 'succeeded' || normalized === 'success') {
    return 'success'
  }

  if (normalized === 'queued' || normalized === 'running' || normalized === 'pending') {
    return 'warning'
  }

  if (normalized === 'failed' || normalized === 'cancelled') {
    return 'danger'
  }

  return 'neutral'
}

function attemptKey(attempt: OperationAttempt, index: number) {
  return attempt.attemptId ?? `attempt:${attempt.startedAtUtc ?? attempt.status ?? 'unknown'}:${index}`
}

function auditRecordKey(record: AuditRecord, index: number) {
  return record.auditRecordId ?? `audit:${record.occurredAtUtc ?? record.action ?? 'unknown'}:${index}`
}
</script>

<template>
  <section class="operation-timeline" aria-labelledby="operation-timeline-title">
    <div class="operation-timeline__header">
      <div>
        <p class="operation-timeline__eyebrow">Operation</p>
        <h1 id="operation-timeline-title" class="operation-timeline__title">
          {{ operationTask?.operationCode ?? 'Task' }}
        </h1>
      </div>
      <UiBadge :tone="badgeTone(operationTask?.status)">
        {{ operationTask?.status ?? (pending ? 'loading' : 'unknown') }}
      </UiBadge>
    </div>

    <p v-if="pending && !operationTask" class="operation-timeline__muted">Loading operation task...</p>

    <template v-else-if="operationTask">
      <dl class="operation-timeline__facts">
        <div class="operation-timeline__fact">
          <dt>Task ID</dt>
          <dd>{{ operationTask.operationTaskId ?? 'Unknown' }}</dd>
        </div>
        <div class="operation-timeline__fact">
          <dt>Instance</dt>
          <dd>{{ operationTask.instanceKey ?? 'Unknown' }}</dd>
        </div>
        <div class="operation-timeline__fact">
          <dt>Requested by</dt>
          <dd>{{ operationTask.requestedBy ?? 'Unknown' }}</dd>
        </div>
        <div class="operation-timeline__fact">
          <dt>Requested at</dt>
          <dd>{{ operationTask.requestedAtUtc ?? 'Unknown' }}</dd>
        </div>
      </dl>

      <section class="operation-timeline__section" aria-labelledby="attempts-title">
        <h2 id="attempts-title" class="operation-timeline__section-title">Attempts</h2>
        <ol v-if="attempts.length" class="operation-timeline__list">
          <li v-for="(attempt, index) in attempts" :key="attemptKey(attempt, index)" class="operation-timeline__item">
            <div class="operation-timeline__item-topline">
              <strong>{{ attempt.attemptId ?? 'Attempt' }}</strong>
              <UiBadge :tone="badgeTone(attempt.status)">
                {{ attempt.status ?? 'unknown' }}
              </UiBadge>
            </div>
            <span class="operation-timeline__muted">{{ attempt.startedAtUtc ?? 'No start time' }}</span>
            <span v-if="attempt.finishedAtUtc" class="operation-timeline__muted">{{ attempt.finishedAtUtc }}</span>
            <span v-if="attempt.failureCode" class="operation-timeline__failure">{{ attempt.failureCode }}</span>
          </li>
        </ol>
        <p v-else class="operation-timeline__muted">No attempts reported yet.</p>
      </section>

      <section class="operation-timeline__section" aria-labelledby="audit-title">
        <h2 id="audit-title" class="operation-timeline__section-title">Audit Records</h2>
        <ol v-if="auditRecords.length" class="operation-timeline__list">
          <li v-for="(record, index) in auditRecords" :key="auditRecordKey(record, index)" class="operation-timeline__item">
            <div class="operation-timeline__item-topline">
              <strong>{{ record.action ?? 'Audit event' }}</strong>
              <span>{{ record.actor ?? 'Unknown actor' }}</span>
            </div>
            <span class="operation-timeline__muted">{{ record.occurredAtUtc ?? 'No timestamp' }}</span>
            <span class="operation-timeline__muted">{{ record.correlationId ?? 'No correlation ID' }}</span>
          </li>
        </ol>
        <p v-else class="operation-timeline__muted">No audit records reported yet.</p>
      </section>
    </template>

    <p v-else class="operation-timeline__muted">Operation task was not found.</p>
  </section>
</template>

<style scoped>
.operation-timeline {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  box-shadow: 0 10px 30px rgb(15 23 42 / 0.06);
  padding: 1.2rem;
}

.operation-timeline__header {
  align-items: center;
  border-bottom: 1px solid var(--color-border);
  display: flex;
  gap: 1rem;
  justify-content: space-between;
  margin: 0 -1.2rem 1rem;
  padding: 0 1.2rem 1rem;
}

.operation-timeline__eyebrow {
  color: var(--color-accent);
  font-size: 0.75rem;
  font-weight: 800;
  letter-spacing: 0;
  margin: 0 0 0.25rem;
  text-transform: uppercase;
}

.operation-timeline__title {
  font-size: 1.35rem;
  line-height: 1.2;
  margin: 0;
}

.operation-timeline__facts {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  margin: 0;
}

.operation-timeline__fact {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 0.45rem;
  display: grid;
  gap: 0.25rem;
  padding: 0.75rem;
}

.operation-timeline__fact dt {
  color: var(--color-text-muted);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

.operation-timeline__fact dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.operation-timeline__section {
  border-top: 1px solid var(--color-border);
  margin-top: 1.1rem;
  padding-top: 1.1rem;
}

.operation-timeline__section-title {
  font-size: 1rem;
  margin: 0 0 0.75rem;
}

.operation-timeline__list {
  display: grid;
  gap: 0.75rem;
  list-style: none;
  margin: 0;
  padding: 0;
}

.operation-timeline__item {
  border-left: 3px solid var(--color-accent);
  display: grid;
  gap: 0.3rem;
  padding: 0.15rem 0 0.15rem 0.8rem;
}

.operation-timeline__item-topline {
  align-items: center;
  display: flex;
  gap: 0.6rem;
  justify-content: space-between;
}

.operation-timeline__muted {
  color: var(--color-text-muted);
  margin: 0;
  overflow-wrap: anywhere;
}

.operation-timeline__failure {
  color: var(--color-danger);
  font-weight: 800;
}

@media (max-width: 920px) {
  .operation-timeline__facts {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 620px) {
  .operation-timeline__facts {
    grid-template-columns: 1fr;
  }

  .operation-timeline__header,
  .operation-timeline__item-topline {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
