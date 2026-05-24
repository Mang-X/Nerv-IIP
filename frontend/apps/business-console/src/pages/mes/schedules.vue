<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesSchedules } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleRunScheduleRequest,
  BusinessConsoleScheduledOperation,
} from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { PlayIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.schedules',
  },
})

const { lastSchedule, runSchedule, runScheduleError, runSchedulePending } = useMesSchedules()

const runSuccess = shallowRef('')

const runForm = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  ruleCode: 'finite-capacity',
  scheduleDate: toDateInput(new Date()),
  workCenterId: 'WC-001',
})

const trigger = computed(() => {
  return [
    `rule=${runForm.ruleCode.trim()}`,
    `date=${runForm.scheduleDate.trim()}`,
    `workCenter=${runForm.workCenterId.trim()}`,
  ].join(';')
})
const assignments = computed(() => lastSchedule.value?.assignments ?? [])
const affectedWorkOrderIds = computed(() => lastSchedule.value?.affectedWorkOrderIds ?? [])
const errorMessage = computed(() => formatError(runScheduleError.value))
const canRunSchedule = computed(
  () =>
    isNonEmpty(runForm.organizationId) &&
    isNonEmpty(runForm.environmentId) &&
    isNonEmpty(runForm.ruleCode) &&
    isNonEmpty(runForm.scheduleDate) &&
    isNonEmpty(runForm.workCenterId),
)

async function submitScheduleRun() {
  if (!canRunSchedule.value) return

  const body: BusinessConsoleRunScheduleRequest = {
    organizationId: runForm.organizationId.trim(),
    environmentId: runForm.environmentId.trim(),
    trigger: trigger.value,
  }

  const response = await runSchedule(body)
  runSuccess.value = `Schedule run ${response?.data?.scheduleVersion ?? body.trigger} completed.`
}

function assignmentKey(item: BusinessConsoleScheduledOperation, index: number) {
  return `${item.workOrderId ?? 'wo'}:${item.operationTaskId ?? index}`
}

function formatDateTime(value?: string | null) {
  if (!value) return 'n/a'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function toDateInput(date: Date) {
  return date.toISOString().slice(0, 10)
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? 'Request failed.' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="Schedules"
        summary="Run rule-based scheduling and review operation assignments as a list."
      />

      <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitScheduleRun">
        <BusinessFormStatus :error="errorMessage" :success="runSuccess" />

        <FieldGroup class="grid gap-3 md:grid-cols-5">
          <Field>
            <FieldLabel for="schedule-org">Organization</FieldLabel>
            <Input id="schedule-org" v-model="runForm.organizationId" required />
          </Field>
          <Field>
            <FieldLabel for="schedule-env">Environment</FieldLabel>
            <Input id="schedule-env" v-model="runForm.environmentId" required />
          </Field>
          <Field>
            <FieldLabel>Rule</FieldLabel>
            <Select v-model="runForm.ruleCode">
              <SelectTrigger aria-label="Schedule rule">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="finite-capacity">Finite capacity</SelectItem>
                <SelectItem value="earliest-due-date">Earliest due date</SelectItem>
                <SelectItem value="rush-priority">Rush priority</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="schedule-date">Schedule date</FieldLabel>
            <Input id="schedule-date" v-model="runForm.scheduleDate" required type="date" />
          </Field>
          <Field>
            <FieldLabel for="schedule-work-center">Work center</FieldLabel>
            <Input id="schedule-work-center" v-model="runForm.workCenterId" required />
          </Field>
        </FieldGroup>

        <div class="flex items-center justify-between gap-3 rounded-lg border p-3">
          <span class="min-w-0 truncate text-sm text-muted-foreground">{{ trigger }}</span>
          <Button type="submit" :disabled="runSchedulePending || !canRunSchedule">
            <Spinner v-if="runSchedulePending" data-icon="inline-start" />
            <PlayIcon v-else data-icon="inline-start" />
            Run schedule
          </Button>
        </div>
      </form>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell
          label="Version"
          :value="lastSchedule?.scheduleVersion ?? 'n/a'"
          :detail="lastSchedule?.trigger ?? 'No run yet'"
        />
        <BusinessMetricCell label="Assignments" :value="assignments.length" detail="Returned rows" />
        <BusinessMetricCell label="Affected orders" :value="affectedWorkOrderIds.length" detail="Impacted work order IDs" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">Schedule result</h2>
          <span class="text-sm text-muted-foreground">{{ formatDateTime(lastSchedule?.scheduledAtUtc) }}</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Work order</TableHead>
                <TableHead>Operation</TableHead>
                <TableHead>Work center</TableHead>
                <TableHead>Start</TableHead>
                <TableHead>End</TableHead>
                <TableHead>Reason</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(assignment, index) in assignments" :key="assignmentKey(assignment, index)">
                <TableCell class="font-medium">{{ assignment.workOrderId ?? 'n/a' }}</TableCell>
                <TableCell>{{ assignment.operationTaskId ?? 'n/a' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ assignment.workCenterId ?? 'n/a' }}</Badge>
                </TableCell>
                <TableCell>{{ formatDateTime(assignment.startUtc) }}</TableCell>
                <TableCell>{{ formatDateTime(assignment.endUtc) }}</TableCell>
                <TableCell>{{ assignment.reason ?? 'n/a' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!assignments.length && !runSchedulePending" :colspan="6">
                No schedule assignments returned.
              </TableEmpty>
              <TableEmpty v-if="runSchedulePending" :colspan="6">Running schedule...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <div v-if="affectedWorkOrderIds.length" class="rounded-lg border bg-background p-4">
        <h2 class="text-sm font-semibold text-foreground">Affected work orders</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <Badge v-for="workOrderId in affectedWorkOrderIds" :key="workOrderId" variant="secondary">
            {{ workOrderId }}
          </Badge>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
