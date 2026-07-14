<script setup lang="ts">
import type { BusinessConsoleInspectionRecordResultLine } from '@nerv-iip/api-client'
import { NvCell } from '@nerv-iip/ui-mobile'

defineProps<{
  lines: BusinessConsoleInspectionRecordResultLine[]
}>()
</script>

<template>
  <section v-if="lines.length > 0" class="space-y-2">
    <h2 class="text-sm font-medium text-muted-foreground">特性结果</h2>
    <div class="overflow-hidden rounded-lg border border-border">
      <NvCell
        v-for="line in lines"
        :key="line.characteristicCode ?? ''"
        data-testid="record-line"
        :title="line.characteristicCode ?? ''"
        :note="line.defectReason ? `原因码 ${line.defectReason}` : undefined"
      >
        <template #value>
          <span :class="line.result === 'passed' ? 'text-foreground' : 'text-destructive'">
            {{ line.measuredValue ?? line.observedValue
            }}{{ line.unitCode ? ` ${line.unitCode}` : '' }} ·
            {{ line.result === 'passed' ? '合格' : '不合格' }}
          </span>
        </template>
      </NvCell>
    </div>
  </section>
</template>
