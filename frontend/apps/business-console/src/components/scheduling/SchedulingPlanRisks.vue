<script setup lang="ts">
import { computed } from 'vue'
import type {
  BusinessConsoleSchedulingMaterialRisk,
  BusinessConsoleSchedulingEquipmentRisk,
} from '@nerv-iip/api-client'

const props = defineProps<{
  materialRisks: BusinessConsoleSchedulingMaterialRisk[]
  equipmentRisks: BusinessConsoleSchedulingEquipmentRisk[]
}>()
const materialGroups = computed(() =>
  Object.entries(Object.groupBy(props.materialRisks, (risk) => risk.orderId ?? '未关联工单')),
)
const equipmentGroups = computed(() =>
  Object.entries(Object.groupBy(props.equipmentRisks, (risk) => risk.resourceId ?? '未命名资源')),
)
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" aria-label="方案冻结风险">
    <header>
      <h3 class="text-sm font-semibold">方案冻结风险</h3>
      <p class="text-sm text-muted-foreground">
        方案生成时的快照，不随 MES 备料更新。当前齐套状态请到 MES
        工单核对；开工前仍需确认齐套和设备可用。
      </p>
    </header>
    <details
      v-if="materialRisks.length"
      class="rounded-md border border-warning/40 p-3 text-sm"
      data-testid="scheduling-material-risks"
    >
      <summary class="cursor-pointer font-medium">
        物料风险 · {{ materialRisks.length }} 道工序 ·
        {{ materialGroups.length }} 个工单（展开明细）
      </summary>
      <details v-for="[orderId, risks] in materialGroups" :key="orderId" class="mt-3">
        <summary class="cursor-pointer font-medium break-all">
          {{ orderId }} · {{ risks?.length }} 道工序
        </summary>
        <article
          v-for="(risk, index) in risks"
          :key="index"
          class="mt-2 rounded-md bg-warning/10 p-3"
          data-testid="plan-material-risk"
        >
          <p class="font-medium break-all">{{ risk.orderId }} · {{ risk.operationId }}</p>
          <ul class="mt-1 grid gap-1 text-xs">
            <li
              v-for="(shortage, shortageIndex) in risk.shortages"
              :key="shortageIndex"
              class="break-all"
            >
              {{ shortage.materialId
              }}<template v-if="shortage.materialLotId">
                · 批次 {{ shortage.materialLotId }}</template
              >
              · 缺 {{ shortage.shortageQuantity }}（需 {{ shortage.requiredQuantity }}，可用
              {{ shortage.availableQuantity }}）
            </li>
          </ul>
          <p class="mt-1 text-xs break-all">{{ risk.message }}</p>
        </article>
      </details>
    </details>
    <p v-else class="text-sm text-muted-foreground">方案未记录物料风险。</p>
    <details
      v-if="equipmentRisks.length"
      class="rounded-md border p-3 text-sm"
      data-testid="scheduling-equipment-risks"
    >
      <summary class="cursor-pointer font-medium">
        设备状态未知 · {{ equipmentRisks.length }} 道工序 ·
        {{ equipmentGroups.length }} 个资源（展开明细）
      </summary>
      <details v-for="[resourceId, risks] in equipmentGroups" :key="resourceId" class="mt-3">
        <summary class="cursor-pointer font-medium break-all">
          {{ resourceId }} · {{ risks?.length }} 道工序
        </summary>
        <p
          v-for="(risk, index) in risks"
          :key="index"
          class="mt-2 break-all text-xs text-muted-foreground"
          data-testid="plan-equipment-risk"
        >
          {{ risk.orderId }} · {{ risk.operationId }} · {{ risk.resourceId }} — {{ risk.message }}
        </p>
      </details>
    </details>
    <p v-else class="text-sm text-muted-foreground">方案未记录设备状态风险。</p>
  </section>
</template>
