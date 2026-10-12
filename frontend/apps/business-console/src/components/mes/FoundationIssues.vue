<script setup lang="ts">
import type { BusinessConsoleMesReadinessIssue } from '@nerv-iip/api-client'
import type { MesFoundationReadinessFilters } from '@/composables/useBusinessMes'
import { computed } from 'vue'
const props = defineProps<{
  issues: BusinessConsoleMesReadinessIssue[]
  filters: MesFoundationReadinessFilters
}>()
const groups = computed(() => {
  const result = new Map<string, BusinessConsoleMesReadinessIssue[]>()
  for (const issue of props.issues) {
    const key = `${issue.code}:${issue.severity}`
    const group = result.get(key) ?? []
    group.push(issue)
    result.set(key, group)
  }
  return [...result.entries()].map(([key, issues]) => ({ key, issues, first: issues[0]! }))
})
const labels: Record<string, string> = {
  WORK_CENTER_COST_RATE_MISSING: '缺工作中心人工费率',
  WORK_CENTER_MACHINE_OVERHEAD_RATE_MISSING: '缺机器制造费用率',
  ACCOUNTING_PERIOD_MISSING: '缺唯一对应的会计期间',
}
function maintenanceUrl(issue: BusinessConsoleMesReadinessIssue) {
  const paths: Record<string, string> = {
    WORK_CENTER_COST_RATE_MISSING: '/erp/finance/work-center-cost-rates',
    WORK_CENTER_MACHINE_OVERHEAD_RATE_MISSING: '/erp/finance/machine-overhead-rates',
    ACCOUNTING_PERIOD_MISSING: '/erp/finance/accounting-period-open',
  }
  const path = paths[issue.code ?? '']
  if (!path) return undefined
  const query = new URLSearchParams({ fromFoundation: '1' })
  for (const [key, value] of Object.entries(props.filters)) {
    if (value) query.set(`foundation${key[0]!.toUpperCase()}${key.slice(1)}`, value)
  }
  if (issue.referenceType === 'WorkCenter' && issue.referenceId)
    query.set('workCenterId', issue.referenceId)
  if (issue.referenceType === 'AccountingPeriod' && issue.referenceId)
    query.set('date', issue.referenceId)
  return `${path}?${query}`
}
</script>
<template>
  <div v-if="groups.length" class="grid gap-3">
    <section v-for="group in groups" :key="group.key" class="grid gap-1">
      <p class="font-medium">{{ labels[group.first.code ?? ''] ?? group.first.message }}</p>
      <p v-if="group.first.fixHint" class="text-xs text-muted-foreground">
        {{ group.first.fixHint }}
      </p>
      <details>
        <summary class="cursor-pointer text-brand">
          查看 {{ group.issues.length }} 个对象明细
        </summary>
        <ul class="mt-2 grid gap-2">
          <li v-for="(issue, index) in group.issues" :key="index">
            <p>{{ issue.message }}</p>
            <a
              v-if="maintenanceUrl(issue)"
              :href="maintenanceUrl(issue)"
              class="text-brand hover:underline"
            >
              去维护{{ issue.referenceDisplayName ? `：${issue.referenceDisplayName}` : '' }}
            </a>
          </li>
        </ul>
      </details>
    </section>
  </div>
  <span v-else class="text-muted-foreground">无问题</span>
</template>
