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
  return error instanceof Error ? error.message : error ? 'Request failed.' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="Inventory"
        title="Availability"
        summary="Query current inventory facts by SKU, site, location, lot and ownership."
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
            Refresh
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-3 xl:grid-cols-5">
          <Field>
            <FieldLabel for="availability-org">Organization</FieldLabel>
            <Input id="availability-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="availability-env">Environment</FieldLabel>
            <Input id="availability-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="availability-sku">SKU</FieldLabel>
            <Input id="availability-sku" v-model="filters.skuCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-uom">UOM</FieldLabel>
            <Input id="availability-uom" v-model="filters.uomCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-site">Site</FieldLabel>
            <Input id="availability-site" v-model="filters.siteCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-location">Location</FieldLabel>
            <Input id="availability-location" v-model="filters.locationCode" />
          </Field>
          <Field>
            <FieldLabel for="availability-lot">Lot</FieldLabel>
            <Input id="availability-lot" v-model="filters.lotNo" />
          </Field>
          <Field>
            <FieldLabel for="availability-serial">Serial</FieldLabel>
            <Input id="availability-serial" v-model="filters.serialNo" />
          </Field>
          <Field>
            <FieldLabel for="availability-quality">Quality</FieldLabel>
            <Input id="availability-quality" v-model="filters.qualityStatus" />
          </Field>
          <Field>
            <FieldLabel>Owner type</FieldLabel>
            <Select v-model="filters.ownerType">
              <SelectTrigger aria-label="Owner type">
                <SelectValue placeholder="Owner type" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="owned">Owned</SelectItem>
                <SelectItem value="customer">Customer</SelectItem>
                <SelectItem value="supplier">Supplier</SelectItem>
                <SelectItem value="consignment">Consignment</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="availability-owner">Owner</FieldLabel>
            <Input id="availability-owner" v-model="filters.ownerId" placeholder="optional" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 sm:grid-cols-3">
        <BusinessMetricCell label="On hand" :value="formatQuantity(onHandQuantity)" :detail="filters.uomCode" />
        <BusinessMetricCell label="Available" :value="formatQuantity(availableQuantity)" :detail="filters.uomCode" />
        <BusinessMetricCell
          label="Frozen / other"
          :value="formatQuantity(frozenQuantity)"
          detail="Derived from returned quantities"
        />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">Availability lines</h2>
          <span class="text-sm text-muted-foreground">{{ availabilityLines.length }} returned</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Location</TableHead>
                <TableHead>Lot / serial</TableHead>
                <TableHead>Quality</TableHead>
                <TableHead>Owner</TableHead>
                <TableHead class="text-right">On hand</TableHead>
                <TableHead class="text-right">Available</TableHead>
                <TableHead class="text-right">Frozen / other</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(line, index) in availabilityLines" :key="`${line.locationCode ?? 'loc'}:${index}`">
                <TableCell class="font-medium">{{ line.locationCode ?? 'n/a' }}</TableCell>
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <span>{{ line.lotNo ?? 'No lot' }}</span>
                    <span class="text-xs text-muted-foreground">{{ line.serialNo ?? 'No serial' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ line.qualityStatus ?? 'unknown' }}</Badge>
                </TableCell>
                <TableCell>{{ line.ownerId ?? line.ownerType ?? 'n/a' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(line.onHandQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(line.availableQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">
                  {{ formatQuantity(lineFrozen(line.onHandQuantity, line.availableQuantity)) }}
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!availabilityLines.length && !availabilityPending" :colspan="7">
                No availability lines returned.
              </TableEmpty>
              <TableEmpty v-if="availabilityPending" :colspan="7">Loading availability...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
