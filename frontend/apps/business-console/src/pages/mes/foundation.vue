<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesFoundationReadiness } from '@/composables/useBusinessMes'
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
    title: '生产准备检查',
  },
})

const { filters, readiness, readinessError, readinessPending, refreshReadiness } =
  useMesFoundationReadiness()

const areas = computed(() => readiness.value?.areas ?? [])
const blockingIssues = computed(() => readiness.value?.blockingIssues ?? [])
const warningIssues = computed(() => readiness.value?.warningIssues ?? [])
const errorMessage = computed(() => formatError(readinessError.value))

function statusLabel(status?: string) {
  if (status === 'Ready') return '就绪'
  if (status === 'Warning') return '警告'
  if (status === 'Blocked') return '阻塞'
  return status ?? '未知'
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
        title="生产准备检查"
        summary="作为开工、释放和派工前的辅助检查，不替代主数据、工程、库存和质量维护入口。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="readinessPending" @click="refreshReadiness">
            <RefreshCwIcon data-icon="inline-start" />
            重新检查
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="foundation-org">组织</FieldLabel>
            <Input id="foundation-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="foundation-env">环境</FieldLabel>
            <Input id="foundation-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="foundation-site">工厂</FieldLabel>
            <Input id="foundation-site" v-model="filters.siteCode" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="foundation-line">产线</FieldLabel>
            <Input id="foundation-line" v-model="filters.lineCode" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="foundation-work-center">工作中心</FieldLabel>
            <Input id="foundation-work-center" v-model="filters.workCenterCode" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="foundation-sku">物料/SKU</FieldLabel>
            <Input id="foundation-sku" v-model="filters.skuId" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="foundation-version">生产版本</FieldLabel>
            <Input id="foundation-version" v-model="filters.productionVersionId" placeholder="可选" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="总状态" :value="statusLabel(readiness?.status)" detail="综合生产准备结果" />
        <BusinessMetricCell label="检查区域" :value="areas.length" detail="当前已纳入的来源" />
        <BusinessMetricCell label="阻塞问题" :value="blockingIssues.length" detail="必须先处理" />
        <BusinessMetricCell label="警告问题" :value="warningIssues.length" detail="建议处理" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">检查结果</h2>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>区域</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>问题</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="area in areas" :key="area.areaCode">
                <TableCell class="font-medium">{{ area.areaCode ?? '未知区域' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ statusLabel(area.status) }}</Badge>
                </TableCell>
                <TableCell>
                  <div class="grid gap-1">
                    <span v-for="issue in area.issues ?? []" :key="`${issue.code}-${issue.referenceId}`">
                      {{ issue.message ?? issue.code ?? '未命名问题' }}
                    </span>
                    <span v-if="!(area.issues?.length)" class="text-muted-foreground">无问题</span>
                  </div>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!areas.length && !readinessPending" :colspan="3">
                暂无检查结果。
              </TableEmpty>
              <TableEmpty v-if="readinessPending" :colspan="3">正在检查基础数据...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
