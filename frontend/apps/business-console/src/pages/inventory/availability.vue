<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useInventoryAvailability } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
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
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.availability',
  },
})

const {
  availability,
  availabilityError,
  availabilityLines,
  availabilityPending,
  filters,
  refreshAvailability,
} = useInventoryAvailability()

const errorMessage = computed(() => formatError(availabilityError.value))
const onHandQuantity = computed(() => availability.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => availability.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => availability.value?.reservedQuantity ?? 0)
const frozenQuantity = computed(() =>
  Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0),
)

function lineFrozen(onHand?: number, available?: number) {
  return Math.max((onHand ?? 0) - (available ?? 0), 0)
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 3,
  }).format(value ?? 0)
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="库存"
        title="库存可用量"
        summary="按 SKU、工厂、库位、批次和货主查询当前库存事实。"
      >
        <template #actions>
          <Button
            size="sm"
            type="button"
            variant="outline"
            :disabled="availabilityPending"
            @click="refreshAvailability"
          >
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-3 xl:grid-cols-5">
          <Field>
            <FieldLabel for="availability-org">组织</FieldLabel>
            <Input id="availability-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="availability-env">环境</FieldLabel>
            <Input id="availability-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="availability-sku">SKU</FieldLabel>
            <Input id="availability-sku" v-model="filters.skuCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-uom">单位</FieldLabel>
            <Input id="availability-uom" v-model="filters.uomCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-site">工厂</FieldLabel>
            <Input id="availability-site" v-model="filters.siteCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-location">库位</FieldLabel>
            <Input id="availability-location" v-model="filters.locationCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-lot">批次</FieldLabel>
            <Input id="availability-lot" v-model="filters.lotNo" />
          </Field>
          <Field>
            <FieldLabel for="availability-serial">序列号</FieldLabel>
            <Input id="availability-serial" v-model="filters.serialNo" />
          </Field>
          <Field>
            <FieldLabel for="availability-quality">质量状态</FieldLabel>
            <Input id="availability-quality" v-model="filters.qualityStatus" />
          </Field>
          <Field>
            <FieldLabel>货主类型</FieldLabel>
            <Select v-model="filters.ownerType">
              <SelectTrigger aria-label="货主类型">
                <SelectValue placeholder="货主类型" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="owned">自有</SelectItem>
                <SelectItem value="customer">客户</SelectItem>
                <SelectItem value="supplier">供应商</SelectItem>
                <SelectItem value="consignment">寄售</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="availability-owner">货主</FieldLabel>
            <Input id="availability-owner" v-model="filters.ownerId" placeholder="可选" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 sm:grid-cols-3">
        <BusinessMetricCell label="现存量" :value="formatQuantity(onHandQuantity)" :detail="filters.uomCode" />
        <BusinessMetricCell label="可用量" :value="formatQuantity(availableQuantity)" :detail="filters.uomCode" />
        <BusinessMetricCell
          label="冻结/其他"
          :value="formatQuantity(frozenQuantity)"
          detail="根据返回数量推导"
        />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">可用量明细</h2>
          <span class="text-sm text-muted-foreground">返回 {{ availabilityLines.length }} 条</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>库位</TableHead>
                <TableHead>批次/序列号</TableHead>
                <TableHead>质量状态</TableHead>
                <TableHead>货主</TableHead>
                <TableHead class="text-right">现存量</TableHead>
                <TableHead class="text-right">可用量</TableHead>
                <TableHead class="text-right">冻结/其他</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(line, index) in availabilityLines" :key="`${line.locationCode ?? 'loc'}:${index}`">
                <TableCell class="font-medium">{{ line.locationCode ?? '无' }}</TableCell>
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <span>{{ line.lotNo ?? '无批次' }}</span>
                    <span class="text-xs text-muted-foreground">{{ line.serialNo ?? '无序列号' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ line.qualityStatus ?? '未知' }}</Badge>
                </TableCell>
                <TableCell>{{ line.ownerId ?? line.ownerType ?? '无' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(line.onHandQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(line.availableQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">
                  {{ formatQuantity(lineFrozen(line.onHandQuantity, line.availableQuantity)) }}
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!availabilityLines.length && !availabilityPending" :colspan="7">
                未返回可用量明细。
              </TableEmpty>
              <TableEmpty v-if="availabilityPending" :colspan="7">正在加载可用量...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
