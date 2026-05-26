<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesProductionPlans } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '生产计划' } })

const { filters, productionPlans, productionPlansError, productionPlansPending, refreshProductionPlans } = useMesProductionPlans()
const errorMessage = computed(() => productionPlansError.value instanceof Error ? productionPlansError.value.message : productionPlansError.value ? '请求失败。' : '')
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="MES" title="生产计划" summary="查看可转入 MES 执行的生产计划和计划就绪状态。">
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="productionPlansPending" @click="refreshProductionPlans">
            <RefreshCwIcon data-icon="inline-start" />刷新
          </Button>
        </template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="plans-org">组织</FieldLabel><Input id="plans-org" v-model="filters.organizationId" /></Field>
          <Field><FieldLabel for="plans-env">环境</FieldLabel><Input id="plans-env" v-model="filters.environmentId" /></Field>
          <Field><FieldLabel for="plans-status">状态</FieldLabel><Input id="plans-status" v-model="filters.status" placeholder="可选" /></Field>
          <Field><FieldLabel for="plans-take">数量上限</FieldLabel><Input id="plans-take" v-model.number="filters.take" type="number" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="计划数" :value="productionPlans.length" detail="当前筛选结果" />
        <BusinessMetricCell label="可转换" :value="productionPlans.filter((x) => x.readinessStatus === 'Ready').length" detail="就绪计划" />
        <BusinessMetricCell label="需处理" :value="productionPlans.filter((x) => x.readinessStatus !== 'Ready').length" detail="预警或阻塞" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>计划号</TableHead><TableHead>来源</TableHead><TableHead>物料</TableHead><TableHead>数量</TableHead><TableHead>就绪</TableHead><TableHead>计划开始</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in productionPlans" :key="row.productionPlanId">
              <TableCell class="font-medium">{{ row.productionPlanId }}</TableCell>
              <TableCell>{{ row.sourceSystem ?? '未指定' }}</TableCell>
              <TableCell>{{ row.skuId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.plannedQuantity ?? 0 }}</TableCell>
              <TableCell>{{ row.readinessStatus ?? '未知' }}</TableCell>
              <TableCell>{{ row.plannedStartUtc ?? '未指定' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="productionPlansPending" :colspan="6">正在加载生产计划...</TableEmpty>
            <TableEmpty v-if="!productionPlans.length && !productionPlansPending" :colspan="6">暂无生产计划。</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </section>
  </BusinessLayout>
</template>
