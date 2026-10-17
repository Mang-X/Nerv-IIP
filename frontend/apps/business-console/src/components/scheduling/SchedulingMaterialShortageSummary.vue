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
        方案生成时冻结的整批物料缺口；不代表当前 MES 齐套状态。
      </p>
    </header>
    <p v-if="!shortages.length" class="text-sm" role="status">方案快照未记录缺料</p>
    <details v-else class="overflow-x-auto">
      <summary class="cursor-pointer text-sm font-medium">
        {{ shortages.length }} 项物料 / 批次缺口（展开汇总）
      </summary>
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
              <details>
                <summary class="cursor-pointer">
                  {{ shortage.affectedOperations?.length ?? 0 }} 道工序（展开查看）
                </summary>
                <ul class="mt-2 grid gap-1">
                  <li
                    v-for="operation in shortage.affectedOperations"
                    :key="`${operation.orderId}:${operation.operationId}`"
                  >
                    {{ operation.orderId }} · {{ operation.operationId }}
                  </li>
                </ul>
              </details>
            </td>
          </tr>
        </tbody>
      </table>
    </details>
  </section>
</template>
