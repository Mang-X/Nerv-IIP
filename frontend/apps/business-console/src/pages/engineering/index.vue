<script setup lang="ts">
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useBusinessProductEngineering } from '@/composables/useBusinessProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { GitBranchIcon, Layers3Icon, RefreshCwIcon, RouteIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.engineering',
  },
})

const {
  boms,
  bomsError,
  bomsPending,
  filters,
  productionVersions,
  productionVersionsError,
  productionVersionsPending,
  refreshEngineering,
  resolvedProductionVersion,
  resolveError,
  resolveFilters,
  resolvePending,
  routings,
  routingsError,
  routingsPending,
} = useBusinessProductEngineering()

const loading = computed(
  () => bomsPending.value || routingsPending.value || productionVersionsPending.value || resolvePending.value,
)
const errorMessage = computed(
  () =>
    formatError(bomsError.value) ||
    formatError(routingsError.value) ||
    formatError(productionVersionsError.value) ||
    formatError(resolveError.value),
)
const releasedBomCount = computed(() => boms.value.filter((item) => isReleased(item.status)).length)
const releasedRoutingCount = computed(() => routings.value.filter((item) => isReleased(item.status)).length)
const activeProductionVersionCount = computed(
  () => productionVersions.value.filter((item) => item.status === 'active').length,
)
const statusOptions = [
  { label: '已发布', value: 'Released' },
  { label: '草稿', value: 'Draft' },
  { label: '已归档', value: 'Archived' },
]
const productionVersionStatusOptions = [
  { label: '有效', value: 'active' },
  { label: '已归档', value: 'archived' },
]

function syncContext() {
  resolveFilters.organizationId = filters.organizationId
  resolveFilters.environmentId = filters.environmentId
}

function isReleased(status?: string) {
  return status?.toLowerCase() === 'released'
}

function formatDate(value?: string | null) {
  return value ? value : '长期'
}

function formatRange(min?: number | null, max?: number | null) {
  if (min === undefined && max === undefined) {
    return '不限'
  }

  return `${min ?? 0} - ${max ?? '不限'}`
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="工程资料"
        title="发布工程版本"
        summary="维护 MES 和 MRP 可消费的 MBOM、工艺路线与生产版本绑定。"
      >
        <template #actions>
          <Button
            size="sm"
            type="button"
            variant="outline"
            :disabled="loading"
            @click="refreshEngineering"
          >
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessContextBar
        v-model:environment-id="filters.environmentId"
        v-model:organization-id="filters.organizationId"
        :show-line="false"
        :show-shift="false"
        :show-site="false"
        :show-work-center="false"
        title="工程版本范围"
        @update:environment-id="syncContext"
        @update:organization-id="syncContext"
      >
        <FieldGroup class="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
          <Field>
            <FieldLabel for="engineering-sku">SKU</FieldLabel>
            <Input
              id="engineering-sku"
              v-model="filters.skuCode"
              placeholder="FG-FRONT-SHOCK"
            />
          </Field>
          <Field>
            <FieldLabel for="engineering-parent">EBOM 父项</FieldLabel>
            <Input
              id="engineering-parent"
              v-model="filters.parentItemCode"
              placeholder="可选"
            />
          </Field>
          <Field>
            <FieldLabel>MBOM 状态</FieldLabel>
            <Select v-model="filters.bomStatus">
              <SelectTrigger aria-label="MBOM 状态">
                <SelectValue placeholder="MBOM 状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel>工艺路线状态</FieldLabel>
            <Select v-model="filters.routingStatus">
              <SelectTrigger aria-label="工艺路线状态">
                <SelectValue placeholder="工艺路线状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel>生产版本状态</FieldLabel>
            <Select v-model="filters.productionVersionStatus">
              <SelectTrigger aria-label="生产版本状态">
                <SelectValue placeholder="生产版本状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="option in productionVersionStatusOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </BusinessContextBar>

      <div class="grid gap-3 sm:grid-cols-3">
        <BusinessMetricCell label="已发布 MBOM" :value="releasedBomCount" detail="按当前筛选" />
        <BusinessMetricCell label="已发布工艺路线" :value="releasedRoutingCount" detail="按当前筛选" />
        <BusinessMetricCell label="有效生产版本" :value="activeProductionVersionCount" detail="可被解析" />
      </div>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <div class="flex items-center gap-2">
              <Layers3Icon class="size-4 text-muted-foreground" />
              <h2 class="text-sm font-semibold text-foreground">MBOM 版本</h2>
            </div>
            <span class="text-sm text-muted-foreground">{{ boms.length }} 条</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>版本号</TableHead>
                  <TableHead>修订</TableHead>
                  <TableHead>父项</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>生效日</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in boms" :key="`${item.bomCode}:${item.revision}`">
                  <TableCell class="font-medium">{{ item.bomCode }}</TableCell>
                  <TableCell>{{ item.revision }}</TableCell>
                  <TableCell>{{ item.parentItemCode }}</TableCell>
                  <TableCell>
                    <BusinessStatusBadge :value="item.status" />
                  </TableCell>
                  <TableCell>{{ formatDate(item.effectiveDate) }}</TableCell>
                </TableRow>
                <TableEmpty v-if="!boms.length && !bomsPending" :colspan="5">
                  当前范围没有 MBOM 版本。
                </TableEmpty>
                <TableEmpty v-if="bomsPending" :colspan="5">正在加载 MBOM...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <div class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <div class="flex items-center gap-2">
              <GitBranchIcon class="size-4 text-muted-foreground" />
              <h2 class="text-sm font-semibold text-foreground">生产版本解析</h2>
            </div>
          </div>
          <div class="grid gap-3 p-4">
            <FieldGroup class="grid gap-3 sm:grid-cols-3">
              <Field class="sm:col-span-3">
                <FieldLabel for="resolve-sku">SKU</FieldLabel>
                <Input id="resolve-sku" v-model="resolveFilters.skuCode" />
              </Field>
              <Field>
                <FieldLabel for="resolve-date">生效日</FieldLabel>
                <Input id="resolve-date" v-model="resolveFilters.effectiveDate" type="date" />
              </Field>
              <Field>
                <FieldLabel for="resolve-lot">批量</FieldLabel>
                <Input id="resolve-lot" v-model.number="resolveFilters.lotSize" min="0" type="number" />
              </Field>
              <Field>
                <FieldLabel>状态</FieldLabel>
                <div class="flex h-9 items-center">
                  <BusinessStatusBadge :value="resolvedProductionVersion?.status ?? '未解析'" />
                </div>
              </Field>
            </FieldGroup>
            <div class="grid gap-2 rounded-md border bg-muted/30 p-3 text-sm">
              <div class="flex justify-between gap-3">
                <span class="text-muted-foreground">生产版本</span>
                <span class="font-medium">{{ resolvedProductionVersion?.productionVersionId ?? '无匹配' }}</span>
              </div>
              <div class="flex justify-between gap-3">
                <span class="text-muted-foreground">MBOM</span>
                <span class="font-medium">{{ resolvedProductionVersion?.mbomVersionId ?? '无' }}</span>
              </div>
              <div class="flex justify-between gap-3">
                <span class="text-muted-foreground">工艺路线</span>
                <span class="font-medium">{{ resolvedProductionVersion?.routingVersionId ?? '无' }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="grid gap-4 xl:grid-cols-2">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <div class="flex items-center gap-2">
              <RouteIcon class="size-4 text-muted-foreground" />
              <h2 class="text-sm font-semibold text-foreground">工艺路线</h2>
            </div>
            <span class="text-sm text-muted-foreground">{{ routings.length }} 条</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>路线号</TableHead>
                  <TableHead>修订</TableHead>
                  <TableHead>SKU</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>生效日</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in routings" :key="`${item.routingCode}:${item.revision}`">
                  <TableCell class="font-medium">{{ item.routingCode }}</TableCell>
                  <TableCell>{{ item.revision }}</TableCell>
                  <TableCell>{{ item.skuCode }}</TableCell>
                  <TableCell>
                    <BusinessStatusBadge :value="item.status" />
                  </TableCell>
                  <TableCell>{{ formatDate(item.effectiveDate) }}</TableCell>
                </TableRow>
                <TableEmpty v-if="!routings.length && !routingsPending" :colspan="5">
                  当前范围没有工艺路线。
                </TableEmpty>
                <TableEmpty v-if="routingsPending" :colspan="5">正在加载工艺路线...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">生产版本绑定</h2>
            <span class="text-sm text-muted-foreground">{{ productionVersions.length }} 条</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>生产版本</TableHead>
                  <TableHead>SKU</TableHead>
                  <TableHead>MBOM / 工艺路线</TableHead>
                  <TableHead>批量</TableHead>
                  <TableHead>状态</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in productionVersions" :key="item.productionVersionId">
                  <TableCell class="font-medium">{{ item.productionVersionId }}</TableCell>
                  <TableCell>{{ item.skuCode }}</TableCell>
                  <TableCell>
                    <div class="flex flex-col gap-0.5">
                      <span>{{ item.mbomVersionId }}</span>
                      <span class="text-xs text-muted-foreground">{{ item.routingVersionId }}</span>
                    </div>
                  </TableCell>
                  <TableCell>{{ formatRange(item.lotSizeMin, item.lotSizeMax) }}</TableCell>
                  <TableCell>
                    <BusinessStatusBadge :value="item.status" />
                  </TableCell>
                </TableRow>
                <TableEmpty v-if="!productionVersions.length && !productionVersionsPending" :colspan="5">
                  当前范围没有生产版本。
                </TableEmpty>
                <TableEmpty v-if="productionVersionsPending" :colspan="5">正在加载生产版本...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
