<script setup lang="ts">
import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingAssignment,
} from '@nerv-iip/api-client'
import { formatDateTime } from '@/utils/format'
import { schedulingPlanStatusLabel } from '@/utils/schedulingPlanPresentation'

defineProps<{
  plan: BusinessConsoleSchedulePlan
  day: string
  workCenter: string
  assignments: BusinessConsoleSchedulingAssignment[]
}>()
</script>

<template>
  <article class="day-sheet">
    <header class="mb-4 grid gap-1">
      <h3 class="text-lg font-semibold">车间日排程单</h3>
      <p class="text-sm">{{ day }} · {{ workCenter === 'all' ? '全部工作中心' : workCenter }}</p>
      <p class="text-sm">方案 {{ plan.planId }} · {{ schedulingPlanStatusLabel(plan.status) }}</p>
    </header>
    <p v-if="!assignments.length" role="status">该日与工作中心没有排程工序。</p>
    <!-- 纸面排程单使用无分页的静态表格，浏览器打印可以跨页重复表头。 -->
    <table v-else class="w-full text-left text-sm">
      <thead>
        <tr>
          <th>工单</th>
          <th>工序</th>
          <th>工作中心</th>
          <th>资源</th>
          <th>起止时间</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="assignment in assignments" :key="assignment.assignmentId">
          <td>{{ assignment.orderId }}</td>
          <td>
            {{ assignment.operationId
            }}<span v-if="assignment.operationSequence">
              · 第 {{ assignment.operationSequence }} 道</span
            >
          </td>
          <td>{{ assignment.workCenterId }}</td>
          <td>{{ assignment.resourceId }}</td>
          <td>
            <template v-if="assignment.segments?.length">
              <p v-for="(segment, index) in assignment.segments" :key="index">
                {{ formatDateTime(segment.startUtc) }} 至 {{ formatDateTime(segment.endUtc) }}
              </p>
            </template>
            <template v-else
              >{{ formatDateTime(assignment.startUtc) }} 至
              {{ formatDateTime(assignment.endUtc) }}</template
            >
          </td>
        </tr>
      </tbody>
    </table>
  </article>
</template>

<style scoped>
@layer app {
  th,
  td {
    padding: 0.5rem;
    border-bottom: 1px solid var(--border);
    vertical-align: top;
  }
  @media print {
    .day-sheet {
      color: black;
      background: white;
    }
    th,
    td {
      border-color: black;
    }
    tr {
      break-inside: avoid;
    }
    thead {
      display: table-header-group;
    }
  }
}
</style>
