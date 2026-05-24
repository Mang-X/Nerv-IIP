<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useInventoryMovement } from '@/composables/useBusinessInventory'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsolePostStockMovementRequest } from '@nerv-iip/api-client'
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
  Spinner,
} from '@nerv-iip/ui'
import { SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.movements',
  },
})

const { postMovement, postMovementError, postMovementPending } = useInventoryMovement()

const form = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  movementType: 'receipt',
  sourceService: 'business-console',
  sourceDocumentId: '',
  sourceDocumentLineId: '',
  idempotencyKey: '',
  skuCode: 'SKU-001',
  uomCode: 'EA',
  siteCode: 'S1',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
  quantity: '1',
})

const successMessage = shallowRef('')
const errorMessage = computed(() => formatError(postMovementError.value))
const canSubmit = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.movementType) &&
    isNonEmpty(form.sourceService) &&
    isNonEmpty(form.sourceDocumentId) &&
    isNonEmpty(form.idempotencyKey) &&
    isNonEmpty(form.skuCode) &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.siteCode) &&
    isNonEmpty(form.locationCode) &&
    toOptionalNumber(form.quantity) !== undefined,
)

async function submitMovement() {
  if (!canSubmit.value) return

  const body: BusinessConsolePostStockMovementRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    movementType: form.movementType,
    sourceService: form.sourceService.trim(),
    sourceDocumentId: form.sourceDocumentId.trim(),
    sourceDocumentLineId: optionalText(form.sourceDocumentLineId),
    idempotencyKey: form.idempotencyKey.trim(),
    skuCode: form.skuCode.trim(),
    uomCode: form.uomCode.trim(),
    siteCode: form.siteCode.trim(),
    locationCode: form.locationCode.trim(),
    lotNo: optionalText(form.lotNo),
    serialNo: optionalText(form.serialNo),
    qualityStatus: form.qualityStatus.trim(),
    ownerType: form.ownerType.trim(),
    ownerId: optionalText(form.ownerId),
    quantity: toOptionalNumber(form.quantity),
  }

  const response = await postMovement(body)
  successMessage.value = `Movement ${response?.data?.movementId ?? body.idempotencyKey} accepted.`
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
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
        domain="Inventory"
        title="Movement posting"
        summary="Post an idempotent inventory movement request through the BusinessGateway facade."
      />

      <form class="grid gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitMovement">
        <BusinessFormStatus :error="errorMessage" :success="successMessage" />

        <FieldGroup class="grid gap-3 md:grid-cols-3">
          <Field>
            <FieldLabel for="movement-org">Organization</FieldLabel>
            <Input id="movement-org" v-model="form.organizationId" required />
          </Field>
          <Field>
            <FieldLabel for="movement-env">Environment</FieldLabel>
            <Input id="movement-env" v-model="form.environmentId" required />
          </Field>
          <Field>
            <FieldLabel>Movement type</FieldLabel>
            <Select v-model="form.movementType">
              <SelectTrigger aria-label="Movement type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="receipt">Receipt</SelectItem>
                <SelectItem value="issue">Issue</SelectItem>
                <SelectItem value="transfer">Transfer</SelectItem>
                <SelectItem value="adjustment">Adjustment</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="movement-source-service">Source service</FieldLabel>
            <Input id="movement-source-service" v-model="form.sourceService" required />
          </Field>
          <Field>
            <FieldLabel for="movement-source-document">Source document</FieldLabel>
            <Input id="movement-source-document" v-model="form.sourceDocumentId" required />
          </Field>
          <Field>
            <FieldLabel for="movement-source-line">Source line</FieldLabel>
            <Input id="movement-source-line" v-model="form.sourceDocumentLineId" />
          </Field>
          <Field>
            <FieldLabel for="movement-idempotency">Idempotency key</FieldLabel>
            <Input id="movement-idempotency" v-model="form.idempotencyKey" required />
          </Field>
          <Field>
            <FieldLabel for="movement-sku">SKU</FieldLabel>
            <Input id="movement-sku" v-model="form.skuCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-uom">UOM</FieldLabel>
            <Input id="movement-uom" v-model="form.uomCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-site">Site</FieldLabel>
            <Input id="movement-site" v-model="form.siteCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-location">Location</FieldLabel>
            <Input id="movement-location" v-model="form.locationCode" required />
          </Field>
          <Field>
            <FieldLabel for="movement-quantity">Quantity</FieldLabel>
            <Input id="movement-quantity" v-model="form.quantity" inputmode="decimal" required type="number" />
          </Field>
          <Field>
            <FieldLabel for="movement-quality">Quality</FieldLabel>
            <Input id="movement-quality" v-model="form.qualityStatus" required />
          </Field>
          <Field>
            <FieldLabel for="movement-owner-type">Owner type</FieldLabel>
            <Input id="movement-owner-type" v-model="form.ownerType" required />
          </Field>
          <Field>
            <FieldLabel for="movement-owner-id">Owner ID</FieldLabel>
            <Input id="movement-owner-id" v-model="form.ownerId" />
          </Field>
          <Field>
            <FieldLabel for="movement-lot">Lot</FieldLabel>
            <Input id="movement-lot" v-model="form.lotNo" />
          </Field>
          <Field>
            <FieldLabel for="movement-serial">Serial</FieldLabel>
            <Input id="movement-serial" v-model="form.serialNo" />
          </Field>
        </FieldGroup>

        <div class="flex justify-end">
          <Button type="submit" :disabled="postMovementPending || !canSubmit">
            <Spinner v-if="postMovementPending" data-icon="inline-start" />
            <SendIcon v-else data-icon="inline-start" />
            Post movement
          </Button>
        </div>
      </form>
    </section>
  </BusinessLayout>
</template>
