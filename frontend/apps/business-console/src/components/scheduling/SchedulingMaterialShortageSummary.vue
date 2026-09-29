<script setup lang="ts">
import type { BusinessConsoleSchedulingMaterialShortageSummary } from '@nerv-iip/api-client'

defineProps<{
  shortages: BusinessConsoleSchedulingMaterialShortageSummary[]
}>()
</script>

<template>
  <section
    class="grid gap-3 rounded-lg border bg-card p-4"
    data-testid="scheduling-material-shortage-summary"
  >
    <header>
      <h3 class="font-semibold">方案级缺料汇总</h3>
      <p class="text-sm text-muted-foreground">
        整批方案的物料缺口与受影响工序，需在开工前完成备料。
      </p>
    </header>
    <p v-if="!shortages.length" class="text-sm" role="status">方案物料齐套 · 无缺料</p>
    <div v-else class="overflow-x-auto">
      <table class="w-full text-left text-sm">
        <thead class="bg-muted text-muted-foreground">
          <tr>
            <th class="p-2 font-medium">物料 / 批次</th>
            <th class="p-2 font-medium">缺口数量</th>
            <th class="p-2 font-medium">受影响工单 / 工序</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(shortage, index) in shortages" :key="index" class="border-t">
            <td class="p-2 align-top">
              <p class="font-medium">{{ shortage.materialId }}</p>
              <p v-if="shortage.materialLotId" class="text-xs text-muted-foreground">
                批次 {{ shortage.materialLotId }}
              </p>
            </td>
            <td class="p-2 align-top tabular-nums">
              {{ shortage.shortageQuantity }} {{ shortage.uomCode }}
            </td>
            <td class="p-2 align-top">
              <ul class="grid gap-1">
                <li
                  v-for="operation in shortage.affectedOperations"
                  :key="`${operation.orderId}:${operation.operationId}`"
                >
                  {{ operation.orderId }} · {{ operation.operationId }}
                </li>
              </ul>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
