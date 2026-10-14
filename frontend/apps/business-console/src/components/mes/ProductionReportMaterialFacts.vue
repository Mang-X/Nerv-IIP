<script setup lang="ts">
import type { BusinessConsoleMesProductionReportDetailResponse } from '@nerv-iip/api-client'

defineProps<{
  facts: NonNullable<BusinessConsoleMesProductionReportDetailResponse['consumedMaterialLots']>
}>()
</script>

<template>
  <section class="space-y-3" aria-label="来源耗料事实">
    <h3 class="text-base font-semibold">来源耗料事实</h3>
    <dl
      v-for="(fact, index) in facts"
      :key="index"
      class="grid grid-cols-2 gap-4 border-t pt-3 text-sm"
    >
      <div>
        <dt class="text-muted-foreground">物料编号</dt>
        <dd class="mt-1 break-all font-medium">{{ fact.materialId }}</dd>
      </div>
      <div>
        <dt class="text-muted-foreground">物料批编号</dt>
        <dd class="mt-1 break-all font-medium">{{ fact.materialLotId }}</dd>
      </div>
      <div>
        <dt class="text-muted-foreground">耗用数量</dt>
        <dd class="mt-1 font-medium">{{ fact.consumedQuantity }}</dd>
      </div>
      <div>
        <dt class="text-muted-foreground">单位</dt>
        <dd class="mt-1 font-medium">{{ fact.uomCode }}</dd>
      </div>
      <div class="col-span-2">
        <dt class="text-muted-foreground">领料申请号</dt>
        <dd class="mt-1 break-all font-medium">{{ fact.materialIssueRequestNo || '—' }}</dd>
      </div>
    </dl>
  </section>
</template>
