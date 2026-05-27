<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesWipSummary } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '在制跟踪',
  },
})

const { filters, refreshWip, wipError, wipPending, wipRows } = useMesWipSummary()

const errorMessage = computed(() => formatError(wipError.value))
const goodQuantity = computed(() =>
  wipRows.value.reduce((total, item) => total + (item.goodQuantity ?? 0), 0),
)
const scrapQuantity = computed(() =>
  wipRows.value.reduce((total, item) => total + (item.scrapQuantity ?? 0), 0),
)

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="在制跟踪"
        summary="按工单和工序查看计划数量、良品、报废和阻塞原因。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="wipPending" @click="refreshWip">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="wip-status">状态</FieldLabel>
            <Input id="wip-status" v-model="filters.status" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="wip-take">数量</FieldLabel>
            <Input id="wip-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="在制行" :value="wipRows.length" detail="工单/工序粒度" />
        <BusinessMetricCell label="良品数" :value="formatQuantity(goodQuantity)" detail="已报工良品" />
        <BusinessMetricCell label="报废数" :value="formatQuantity(scrapQuantity)" detail="已报工报废" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>工单</TableHead>
                <TableHead>工序任务</TableHead>
                <TableHead>工作中心</TableHead>
                <TableHead>状态</TableHead>
                <TableHead class="text-right">计划数</TableHead>
                <TableHead class="text-right">良品</TableHead>
                <TableHead class="text-right">报废</TableHead>
                <TableHead>阻塞原因</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in wipRows" :key="`${row.workOrderId}-${row.operationTaskId}`">
                <TableCell class="font-medium">{{ row.workOrderId ?? '无' }}</TableCell>
                <TableCell>{{ row.operationTaskId ?? '无' }}</TableCell>
                <TableCell>{{ row.workCenterId ?? '无' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ row.status ?? '未知' }}</Badge>
                </TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.plannedQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.goodQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.scrapQuantity) }}</TableCell>
                <TableCell>{{ row.blockingReasons?.join('，') || '无' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!wipRows.length && !wipPending" :colspan="8">暂无在制数据。</TableEmpty>
              <TableEmpty v-if="wipPending" :colspan="8">正在加载在制状态...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
