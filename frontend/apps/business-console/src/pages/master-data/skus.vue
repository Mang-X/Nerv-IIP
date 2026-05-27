<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useBusinessSkus } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsoleCreateSkuRequest, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Checkbox,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.skus',
  },
})

const {
  createSku,
  createSkuError,
  createSkuPending,
  filters,
  refreshSkus,
  skus,
  skusError,
  skusPending,
} = useBusinessSkus()

const createOpen = shallowRef(false)
const createSuccess = shallowRef('')

const createForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  code: '',
  name: '',
  baseUomCode: 'EA',
  category: '',
  materialType: 'finished-good',
  batchTrackingPolicy: 'none',
  serialTrackingPolicy: 'none',
  shelfLifePolicyCode: 'none',
  storageConditionCode: 'ambient',
  defaultBarcodeRuleCode: 'default',
  qualityRequired: false,
  complianceTags: '',
})

const filteredSkus = computed(() => skus.value)
const createErrorMessage = computed(() => formatError(createSkuError.value))
const listErrorMessage = computed(() => formatError(skusError.value))

const canCreateSku = computed(
  () =>
    isNonEmpty(createForm.organizationId) &&
    isNonEmpty(createForm.environmentId) &&
    isNonEmpty(createForm.code) &&
    isNonEmpty(createForm.name) &&
    isNonEmpty(createForm.baseUomCode) &&
    isNonEmpty(createForm.category) &&
    isNonEmpty(createForm.materialType) &&
    isNonEmpty(createForm.batchTrackingPolicy) &&
    isNonEmpty(createForm.serialTrackingPolicy) &&
    isNonEmpty(createForm.shelfLifePolicyCode) &&
    isNonEmpty(createForm.storageConditionCode) &&
    isNonEmpty(createForm.defaultBarcodeRuleCode),
)

function splitTags(value: string) {
  const tags = value
    .split(',')
    .map((tag) => tag.trim())
    .filter(Boolean)

  return tags.length ? tags : undefined
}

function resetCreateForm() {
  createForm.code = ''
  createForm.name = ''
  createForm.baseUomCode = 'EA'
  createForm.category = ''
  createForm.materialType = 'finished-good'
  createForm.batchTrackingPolicy = 'none'
  createForm.serialTrackingPolicy = 'none'
  createForm.shelfLifePolicyCode = 'none'
  createForm.storageConditionCode = 'ambient'
  createForm.defaultBarcodeRuleCode = 'default'
  createForm.qualityRequired = false
  createForm.complianceTags = ''
}

async function submitSku() {
  if (!canCreateSku.value) return

  const body: BusinessConsoleCreateSkuRequest = {
    organizationId: createForm.organizationId.trim(),
    environmentId: createForm.environmentId.trim(),
    code: createForm.code.trim(),
    name: createForm.name.trim(),
    baseUomCode: createForm.baseUomCode.trim(),
    category: createForm.category.trim(),
    materialType: createForm.materialType.trim(),
    batchTrackingPolicy: createForm.batchTrackingPolicy.trim(),
    serialTrackingPolicy: createForm.serialTrackingPolicy.trim(),
    shelfLifePolicyCode: createForm.shelfLifePolicyCode.trim(),
    storageConditionCode: createForm.storageConditionCode.trim(),
    defaultBarcodeRuleCode: createForm.defaultBarcodeRuleCode.trim(),
    qualityRequired: createForm.qualityRequired,
    complianceTags: splitTags(createForm.complianceTags),
  }

  const response = await createSku(body)
  createSuccess.value = `SKU ${response?.data?.code ?? body.code} 已提交。`
  resetCreateForm()
  createOpen.value = false
}

function syncContextFromFilters() {
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
}

function rowKey(item: BusinessConsoleResourceItem, index: number) {
  return `${item.resourceType ?? 'sku'}:${item.code ?? index}`
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="主数据"
        title="SKU 维护"
        summary="通过业务网关查询和创建 SKU 主数据。"
      >
        <template #actions>
          <Button
            size="sm"
            variant="outline"
            type="button"
            :disabled="skusPending"
            @click="refreshSkus"
          >
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>

          <Dialog v-model:open="createOpen" @update:open="syncContextFromFilters">
            <DialogTrigger as-child>
              <Button size="sm" type="button">
                <PlusIcon data-icon="inline-start" />
                新建 SKU
              </Button>
            </DialogTrigger>
            <DialogContent class="sm:max-w-3xl">
              <DialogHeader>
                <DialogTitle>新建 SKU</DialogTitle>
                <DialogDescription>
                  提交新的 SKU 主数据，必填项与业务网关契约保持一致。
                </DialogDescription>
              </DialogHeader>

              <form class="grid gap-4" @submit.prevent="submitSku">
                <BusinessFormStatus :error="createErrorMessage" />

                <FieldGroup class="grid gap-3 sm:grid-cols-2">
                  <Field>
                    <FieldLabel for="sku-code">SKU 编码</FieldLabel>
                    <Input id="sku-code" v-model="createForm.code" autocomplete="off" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-name">名称</FieldLabel>
                    <Input id="sku-name" v-model="createForm.name" autocomplete="off" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-uom">基本单位</FieldLabel>
                    <Input id="sku-uom" v-model="createForm.baseUomCode" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-category">分类</FieldLabel>
                    <Input id="sku-category" v-model="createForm.category" required />
                  </Field>
                  <Field>
                    <FieldLabel>物料类型</FieldLabel>
                    <Select v-model="createForm.materialType">
                      <SelectTrigger aria-label="物料类型">
                        <SelectValue placeholder="物料类型" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="finished-good">成品</SelectItem>
                        <SelectItem value="raw-material">原材料</SelectItem>
                        <SelectItem value="packaging">包材</SelectItem>
                        <SelectItem value="service">服务</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>批次追踪</FieldLabel>
                    <Select v-model="createForm.batchTrackingPolicy">
                      <SelectTrigger aria-label="批次追踪">
                        <SelectValue placeholder="批次追踪" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="none">不追踪</SelectItem>
                        <SelectItem value="optional">可选</SelectItem>
                        <SelectItem value="required">必填</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>序列号追踪</FieldLabel>
                    <Select v-model="createForm.serialTrackingPolicy">
                      <SelectTrigger aria-label="序列号追踪">
                        <SelectValue placeholder="序列号追踪" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="none">不追踪</SelectItem>
                        <SelectItem value="optional">可选</SelectItem>
                        <SelectItem value="required">必填</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="sku-shelf">保质期策略</FieldLabel>
                    <Input id="sku-shelf" v-model="createForm.shelfLifePolicyCode" />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-storage">存储条件</FieldLabel>
                    <Input id="sku-storage" v-model="createForm.storageConditionCode" />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-barcode">默认条码规则</FieldLabel>
                    <Input id="sku-barcode" v-model="createForm.defaultBarcodeRuleCode" />
                  </Field>
                  <Field class="sm:col-span-2">
                    <FieldLabel for="sku-tags">合规标签</FieldLabel>
                    <Input id="sku-tags" v-model="createForm.complianceTags" placeholder="GMP, 出口" />
                  </Field>
                  <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3 sm:col-span-2">
                    <FieldLabel for="sku-quality">需要质量检验</FieldLabel>
                    <Checkbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                  </Field>
                </FieldGroup>

                <DialogFooter>
                  <Button type="submit" :disabled="createSkuPending || !canCreateSku">
                    <Spinner v-if="createSkuPending" data-icon="inline-start" />
                    新建 SKU
                  </Button>
                </DialogFooter>
              </form>
            </DialogContent>
          </Dialog>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <div class="grid gap-3 sm:grid-cols-3">
          <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
            <FieldLabel for="sku-include-disabled">包含停用数据</FieldLabel>
            <Checkbox id="sku-include-disabled" v-model:checked="filters.includeDisabled" />
          </Field>
        </div>
        <BusinessFormStatus :error="listErrorMessage" :success="createSuccess" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">SKU 列表</h2>
          <span class="text-sm text-muted-foreground">返回 {{ filteredSkus.length }} 条</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>编码</TableHead>
                <TableHead>显示名称</TableHead>
                <TableHead>资源类型</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>快照版本</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(sku, index) in filteredSkus" :key="rowKey(sku, index)">
                <TableCell class="font-medium">{{ sku.code ?? '无' }}</TableCell>
                <TableCell>{{ sku.displayName ?? '无' }}</TableCell>
                <TableCell>{{ sku.resourceType ?? 'sku' }}</TableCell>
                <TableCell>
                  <Badge :variant="sku.active === false ? 'secondary' : 'success'">
                    {{ sku.active === false ? '停用' : '启用' }}
                  </Badge>
                </TableCell>
                <TableCell class="tabular-nums">{{ sku.snapshotVersion ?? '无' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!filteredSkus.length && !skusPending" :colspan="5">
                未返回 SKU 数据。
              </TableEmpty>
              <TableEmpty v-if="skusPending" :colspan="5">正在加载 SKU...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
