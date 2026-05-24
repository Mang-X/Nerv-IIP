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
  shelfLifePolicyCode: '',
  storageConditionCode: '',
  defaultBarcodeRuleCode: '',
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
    isNonEmpty(createForm.baseUomCode),
)

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

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
  createForm.shelfLifePolicyCode = ''
  createForm.storageConditionCode = ''
  createForm.defaultBarcodeRuleCode = ''
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
    category: optionalText(createForm.category),
    materialType: optionalText(createForm.materialType),
    batchTrackingPolicy: optionalText(createForm.batchTrackingPolicy),
    serialTrackingPolicy: optionalText(createForm.serialTrackingPolicy),
    shelfLifePolicyCode: optionalText(createForm.shelfLifePolicyCode),
    storageConditionCode: optionalText(createForm.storageConditionCode),
    defaultBarcodeRuleCode: optionalText(createForm.defaultBarcodeRuleCode),
    qualityRequired: createForm.qualityRequired,
    complianceTags: splitTags(createForm.complianceTags),
  }

  const response = await createSku(body)
  createSuccess.value = `SKU ${response?.data?.code ?? body.code} submitted.`
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
  return error instanceof Error ? error.message : error ? 'Request failed.' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MasterData"
        title="SKU maintenance"
        summary="List and create SKU master records through the BusinessGateway facade."
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
            Refresh
          </Button>

          <Dialog v-model:open="createOpen" @update:open="syncContextFromFilters">
            <DialogTrigger as-child>
              <Button size="sm" type="button">
                <PlusIcon data-icon="inline-start" />
                Create SKU
              </Button>
            </DialogTrigger>
            <DialogContent class="sm:max-w-3xl">
              <DialogHeader>
                <DialogTitle>Create SKU</DialogTitle>
                <DialogDescription>
                  Submit a new MasterData SKU. Required fields mirror the BFF contract.
                </DialogDescription>
              </DialogHeader>

              <form class="grid gap-4" @submit.prevent="submitSku">
                <BusinessFormStatus :error="createErrorMessage" />

                <FieldGroup class="grid gap-3 sm:grid-cols-2">
                  <Field>
                    <FieldLabel for="sku-org">Organization</FieldLabel>
                    <Input id="sku-org" v-model="createForm.organizationId" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-env">Environment</FieldLabel>
                    <Input id="sku-env" v-model="createForm.environmentId" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-code">SKU code</FieldLabel>
                    <Input id="sku-code" v-model="createForm.code" autocomplete="off" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-name">Name</FieldLabel>
                    <Input id="sku-name" v-model="createForm.name" autocomplete="off" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-uom">Base UOM</FieldLabel>
                    <Input id="sku-uom" v-model="createForm.baseUomCode" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-category">Category</FieldLabel>
                    <Input id="sku-category" v-model="createForm.category" />
                  </Field>
                  <Field>
                    <FieldLabel>Material type</FieldLabel>
                    <Select v-model="createForm.materialType">
                      <SelectTrigger aria-label="Material type">
                        <SelectValue placeholder="Material type" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="finished-good">Finished good</SelectItem>
                        <SelectItem value="raw-material">Raw material</SelectItem>
                        <SelectItem value="packaging">Packaging</SelectItem>
                        <SelectItem value="service">Service</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>Batch tracking</FieldLabel>
                    <Select v-model="createForm.batchTrackingPolicy">
                      <SelectTrigger aria-label="Batch tracking">
                        <SelectValue placeholder="Batch tracking" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="none">None</SelectItem>
                        <SelectItem value="optional">Optional</SelectItem>
                        <SelectItem value="required">Required</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>Serial tracking</FieldLabel>
                    <Select v-model="createForm.serialTrackingPolicy">
                      <SelectTrigger aria-label="Serial tracking">
                        <SelectValue placeholder="Serial tracking" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="none">None</SelectItem>
                        <SelectItem value="optional">Optional</SelectItem>
                        <SelectItem value="required">Required</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="sku-shelf">Shelf-life policy</FieldLabel>
                    <Input id="sku-shelf" v-model="createForm.shelfLifePolicyCode" />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-storage">Storage condition</FieldLabel>
                    <Input id="sku-storage" v-model="createForm.storageConditionCode" />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-barcode">Default barcode rule</FieldLabel>
                    <Input id="sku-barcode" v-model="createForm.defaultBarcodeRuleCode" />
                  </Field>
                  <Field class="sm:col-span-2">
                    <FieldLabel for="sku-tags">Compliance tags</FieldLabel>
                    <Input id="sku-tags" v-model="createForm.complianceTags" placeholder="GMP, export" />
                  </Field>
                  <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3 sm:col-span-2">
                    <FieldLabel for="sku-quality">Quality inspection required</FieldLabel>
                    <Checkbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                  </Field>
                </FieldGroup>

                <DialogFooter>
                  <Button type="submit" :disabled="createSkuPending || !canCreateSku">
                    <Spinner v-if="createSkuPending" data-icon="inline-start" />
                    Create SKU
                  </Button>
                </DialogFooter>
              </form>
            </DialogContent>
          </Dialog>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <div class="grid gap-3 sm:grid-cols-3">
          <Field>
            <FieldLabel for="sku-filter-org">Organization</FieldLabel>
            <Input id="sku-filter-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="sku-filter-env">Environment</FieldLabel>
            <Input id="sku-filter-env" v-model="filters.environmentId" />
          </Field>
          <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
            <FieldLabel for="sku-include-disabled">Include disabled</FieldLabel>
            <Checkbox id="sku-include-disabled" v-model:checked="filters.includeDisabled" />
          </Field>
        </div>
        <BusinessFormStatus :error="listErrorMessage" :success="createSuccess" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">SKU list</h2>
          <span class="text-sm text-muted-foreground">{{ filteredSkus.length }} returned</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Code</TableHead>
                <TableHead>Display name</TableHead>
                <TableHead>Resource type</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Snapshot</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(sku, index) in filteredSkus" :key="rowKey(sku, index)">
                <TableCell class="font-medium">{{ sku.code ?? 'n/a' }}</TableCell>
                <TableCell>{{ sku.displayName ?? 'n/a' }}</TableCell>
                <TableCell>{{ sku.resourceType ?? 'sku' }}</TableCell>
                <TableCell>
                  <Badge :variant="sku.active === false ? 'secondary' : 'success'">
                    {{ sku.active === false ? 'inactive' : 'active' }}
                  </Badge>
                </TableCell>
                <TableCell class="tabular-nums">{{ sku.snapshotVersion ?? 'n/a' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!filteredSkus.length && !skusPending" :colspan="5">
                No SKU data returned.
              </TableEmpty>
              <TableEmpty v-if="skusPending" :colspan="5">Loading SKUs...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
