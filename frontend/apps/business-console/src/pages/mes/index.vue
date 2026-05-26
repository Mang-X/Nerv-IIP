<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesOverview } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Spinner,
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
    title: '生产驾驶舱',
  },
})

const {
  blockers,
  counts,
  filters,
  overviewError,
  overviewPending,
  pendingWork,
  refreshOverview,
} = useMesOverview()

const errorMessage = computed(() => formatError(overviewError.value))

function countValue(key: string) {
  return counts.value.find((item) => item.key === key)?.count ?? 0
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
        title="生产驾驶舱"
        summary="集中查看工单、工序、在制、阻塞和角色待办，作为班组长/调度员的首屏。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="overviewPending" @click="refreshOverview">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-2">
          <Field>
            <FieldLabel for="mes-overview-org">组织</FieldLabel>
            <Input id="mes-overview-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="mes-overview-env">环境</FieldLabel>
            <Input id="mes-overview-env" v-model="filters.environmentId" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="工单" :value="countValue('WorkOrders')" detail="当前可见工单数" />
        <BusinessMetricCell label="工序任务" :value="countValue('OperationTasks')" detail="当前可见任务数" />
        <BusinessMetricCell label="阻塞项" :value="blockers.length" detail="需处理的问题分类" />
        <BusinessMetricCell label="待办" :value="pendingWork.length" detail="按角色汇总" />
      </div>

      <div class="grid gap-4 xl:grid-cols-2">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">阻塞摘要</h2>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>区域</TableHead>
                  <TableHead>代码</TableHead>
                  <TableHead>说明</TableHead>
                  <TableHead class="text-right">数量</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in blockers" :key="`${item.areaCode}-${item.code}`">
                  <TableCell>
                    <Badge variant="secondary">{{ item.areaCode ?? '未知' }}</Badge>
                  </TableCell>
                  <TableCell>{{ item.code ?? '未知' }}</TableCell>
                  <TableCell>{{ item.message ?? '无说明' }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ item.count ?? 0 }}</TableCell>
                </TableRow>
                <TableEmpty v-if="!blockers.length && !overviewPending" :colspan="4">
                  暂无阻塞项。
                </TableEmpty>
                <TableEmpty v-if="overviewPending" :colspan="4">正在加载总览...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">待处理工作</h2>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>角色</TableHead>
                  <TableHead>工作类型</TableHead>
                  <TableHead>入口</TableHead>
                  <TableHead class="text-right">数量</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in pendingWork" :key="`${item.roleCode}-${item.workType}`">
                  <TableCell>{{ item.roleCode ?? '未分配' }}</TableCell>
                  <TableCell>{{ item.workType ?? '待处理' }}</TableCell>
                  <TableCell>{{ item.routeHint ?? '无' }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ item.count ?? 0 }}</TableCell>
                </TableRow>
                <TableEmpty v-if="!pendingWork.length && !overviewPending" :colspan="4">
                  暂无待处理工作。
                </TableEmpty>
                <TableEmpty v-if="overviewPending" :colspan="4">正在加载待办...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
