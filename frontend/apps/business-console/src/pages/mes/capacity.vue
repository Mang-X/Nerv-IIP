<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesCapacityImpacts } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  DataTablePagination,
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
import { computed, ref, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '产能影响',
  },
})

const {
  capacityImpacts,
  capacityImpactsError,
  capacityImpactsPending,
  capacityImpactsTotal,
  filters,
  refreshCapacityImpacts,
} = useMesCapacityImpacts()

const errorMessage = computed(() => formatError(capacityImpactsError.value))
const activeCount = computed(() => capacityImpacts.value.filter((item) => item.status === 'Active').length)
const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)

watch(() => filters.status, () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="生产执行"
        title="产能影响"
        summary="查看设备停机、恢复和维护事件对工作中心产能和排程的影响。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="capacityImpactsPending" @click="refreshCapacityImpacts">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="capacity-status">状态</FieldLabel>
            <Input id="capacity-status" v-model="filters.status" placeholder="可选" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="影响记录" :value="capacityImpactsTotal" detail="后端筛选总数" />
        <BusinessMetricCell label="生效中" :value="activeCount" detail="Active 状态" />
        <BusinessMetricCell label="已结束" :value="capacityImpacts.length - activeCount" detail="非 Active 状态" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>影响编号</TableHead>
                <TableHead>工作中心</TableHead>
                <TableHead>设备</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>开始</TableHead>
                <TableHead>结束</TableHead>
                <TableHead>原因</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in capacityImpacts" :key="row.impactId">
                <TableCell class="font-medium">{{ row.impactId ?? '无' }}</TableCell>
                <TableCell>{{ row.workCenterId ?? '无' }}</TableCell>
                <TableCell>{{ row.deviceAssetId ?? '未指定' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ row.status ?? '未知' }}</Badge>
                </TableCell>
                <TableCell>{{ formatDateTime(row.effectiveFromUtc) }}</TableCell>
                <TableCell>{{ formatDateTime(row.effectiveToUtc) }}</TableCell>
                <TableCell>{{ row.reasonCode ?? '无' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!capacityImpacts.length && !capacityImpactsPending" :colspan="7">
                暂无产能影响。
              </TableEmpty>
              <TableEmpty v-if="capacityImpactsPending" :colspan="7">正在加载产能影响...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
      <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="capacityImpactsTotal" />
    </section>
  </BusinessLayout>
</template>
