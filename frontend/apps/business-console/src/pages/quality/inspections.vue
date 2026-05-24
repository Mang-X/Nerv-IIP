<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useQualityInspectionPlans } from '@/composables/useBusinessQuality'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleInspectionCharacteristicResult,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
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
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.inspections',
  },
})

const {
  createInspectionRecord,
  createInspectionRecordError,
  createInspectionRecordPending,
  filters,
  inspectionPlans,
  inspectionPlansError,
  inspectionPlansPending,
  refreshInspectionPlans,
} = useQualityInspectionPlans()

const recordSuccess = shallowRef('')

const recordForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  inspectionPlanId: '',
  sourceType: 'manufacturing',
  sourceService: 'business-console',
  sourceDocumentId: '',
  skuCode: 'SKU-001',
  inspectedQuantity: '1',
  batchNo: '',
  serialNo: '',
  dispositionReason: '',
  dispositionAttachmentFileIds: '',
  resultLines: [
    {
      characteristicCode: '',
      result: 'pass',
      measuredValue: '',
    },
  ],
})

const listErrorMessage = computed(() => formatError(inspectionPlansError.value))
const createErrorMessage = computed(() => formatError(createInspectionRecordError.value))
const validResultLines = computed(() =>
  recordForm.resultLines.filter(
    (line) => isNonEmpty(line.characteristicCode) && isNonEmpty(line.result),
  ),
)
const canCreateRecord = computed(
  () =>
    isNonEmpty(recordForm.organizationId) &&
    isNonEmpty(recordForm.environmentId) &&
    isNonEmpty(recordForm.sourceType) &&
    isNonEmpty(recordForm.sourceService) &&
    isNonEmpty(recordForm.sourceDocumentId) &&
    isNonEmpty(recordForm.skuCode) &&
    toOptionalNumber(recordForm.inspectedQuantity) !== undefined &&
    validResultLines.value.length > 0,
)

function syncContextFromFilters() {
  recordForm.organizationId = filters.organizationId
  recordForm.environmentId = filters.environmentId
}

function addCharacteristicRow() {
  recordForm.resultLines.push({
    characteristicCode: '',
    result: 'pass',
    measuredValue: '',
  })
}

function removeCharacteristicRow(index: number) {
  if (recordForm.resultLines.length === 1) {
    recordForm.resultLines[0] = {
      characteristicCode: '',
      result: 'pass',
      measuredValue: '',
    }
    return
  }

  recordForm.resultLines.splice(index, 1)
}

async function submitInspectionRecord() {
  if (!canCreateRecord.value) return

  const body: BusinessConsoleCreateInspectionRecordRequest = {
    organizationId: recordForm.organizationId.trim(),
    environmentId: recordForm.environmentId.trim(),
    inspectionPlanId: optionalText(recordForm.inspectionPlanId),
    sourceType: recordForm.sourceType.trim(),
    sourceService: recordForm.sourceService.trim(),
    sourceDocumentId: recordForm.sourceDocumentId.trim(),
    skuCode: recordForm.skuCode.trim(),
    inspectedQuantity: toOptionalNumber(recordForm.inspectedQuantity),
    batchNo: optionalText(recordForm.batchNo),
    serialNo: optionalText(recordForm.serialNo),
    resultLines: toCharacteristicResults(),
    dispositionReason: optionalText(recordForm.dispositionReason),
    dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
  }

  const response = await createInspectionRecord(body)
  recordSuccess.value = `Inspection record ${response?.data?.inspectionRecordId ?? body.sourceDocumentId} submitted.`
}

function toCharacteristicResults(): BusinessConsoleInspectionCharacteristicResult[] {
  return validResultLines.value.map((line) => ({
    characteristicCode: line.characteristicCode.trim(),
    result: line.result.trim(),
    measuredValue: optionalText(line.measuredValue),
  }))
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}

function splitCsv(value: string) {
  const values = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)

  return values.length ? values : undefined
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function rowKey(item: BusinessConsoleQualityItem, index: number) {
  return `${item.id ?? item.code ?? 'plan'}:${index}`
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
        domain="Quality"
        title="Inspections"
        summary="Review inspection plans and submit inspection records through the Quality facade."
      >
        <template #actions>
          <Button
            size="sm"
            variant="outline"
            type="button"
            :disabled="inspectionPlansPending"
            @click="refreshInspectionPlans"
          >
            <RefreshCwIcon data-icon="inline-start" />
            Refresh
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="inspection-org">Organization</FieldLabel>
            <Input id="inspection-org" v-model="filters.organizationId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="inspection-env">Environment</FieldLabel>
            <Input id="inspection-env" v-model="filters.environmentId" @change="syncContextFromFilters" />
          </Field>
          <Field>
            <FieldLabel for="inspection-status">Status</FieldLabel>
            <Input id="inspection-status" v-model="filters.status" placeholder="optional" />
          </Field>
          <Field>
            <FieldLabel for="inspection-take">Take</FieldLabel>
            <Input id="inspection-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </div>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(380px,0.95fr)]">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">Inspection plans</h2>
            <span class="text-sm text-muted-foreground">{{ inspectionPlans.length }} returned</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Plan</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Summary</TableHead>
                  <TableHead class="text-right">Use</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="(plan, index) in inspectionPlans" :key="rowKey(plan, index)">
                  <TableCell>
                    <div class="flex flex-col gap-0.5">
                      <span class="font-medium">{{ plan.code ?? 'n/a' }}</span>
                      <span class="text-xs text-muted-foreground">{{ plan.id ?? 'No plan id' }}</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{{ plan.status ?? 'unknown' }}</Badge>
                  </TableCell>
                  <TableCell>{{ plan.summary ?? 'n/a' }}</TableCell>
                  <TableCell class="text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      type="button"
                      @click="recordForm.inspectionPlanId = plan.id ?? ''"
                    >
                      Select
                    </Button>
                  </TableCell>
                </TableRow>
                <TableEmpty v-if="!inspectionPlans.length && !inspectionPlansPending" :colspan="4">
                  No inspection plans returned.
                </TableEmpty>
                <TableEmpty v-if="inspectionPlansPending" :colspan="4">Loading inspection plans...</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <form class="grid content-start gap-4 rounded-lg border bg-background p-4" @submit.prevent="submitInspectionRecord">
          <div>
            <p class="text-xs font-bold uppercase text-primary">Record</p>
            <h2 class="text-base font-semibold text-foreground">Create inspection record</h2>
          </div>

          <BusinessFormStatus :error="createErrorMessage" :success="recordSuccess" />

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="record-org">Organization</FieldLabel>
              <Input id="record-org" v-model="recordForm.organizationId" required />
            </Field>
            <Field>
              <FieldLabel for="record-env">Environment</FieldLabel>
              <Input id="record-env" v-model="recordForm.environmentId" required />
            </Field>
            <Field>
              <FieldLabel for="record-plan">Inspection plan ID</FieldLabel>
              <Input id="record-plan" v-model="recordForm.inspectionPlanId" />
            </Field>
            <Field>
              <FieldLabel>Source type</FieldLabel>
              <Select v-model="recordForm.sourceType">
                <SelectTrigger aria-label="Source type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="manufacturing">Manufacturing</SelectItem>
                  <SelectItem value="receiving">Receiving</SelectItem>
                  <SelectItem value="final">Final</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="record-source-service">Source service</FieldLabel>
              <Input id="record-source-service" v-model="recordForm.sourceService" required />
            </Field>
            <Field>
              <FieldLabel for="record-source-document">Source document</FieldLabel>
              <Input id="record-source-document" v-model="recordForm.sourceDocumentId" required />
            </Field>
            <Field>
              <FieldLabel for="record-sku">SKU</FieldLabel>
              <Input id="record-sku" v-model="recordForm.skuCode" required />
            </Field>
            <Field>
              <FieldLabel for="record-quantity">Inspected quantity</FieldLabel>
              <Input
                id="record-quantity"
                v-model="recordForm.inspectedQuantity"
                inputmode="decimal"
                required
                type="number"
              />
            </Field>
            <Field>
              <FieldLabel for="record-batch">Batch</FieldLabel>
              <Input id="record-batch" v-model="recordForm.batchNo" />
            </Field>
            <Field>
              <FieldLabel for="record-serial">Serial</FieldLabel>
              <Input id="record-serial" v-model="recordForm.serialNo" />
            </Field>
          </FieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-foreground">Characteristics</h3>
              <Button size="sm" variant="outline" type="button" @click="addCharacteristicRow">
                <PlusIcon data-icon="inline-start" />
                Add row
              </Button>
            </div>

            <div class="grid gap-2">
              <div
                v-for="(line, index) in recordForm.resultLines"
                :key="index"
                class="grid gap-2 rounded-lg border p-3 md:grid-cols-[1fr_130px_1fr_auto]"
              >
                <Field>
                  <FieldLabel :for="`characteristic-code-${index}`">Characteristic code</FieldLabel>
                  <Input :id="`characteristic-code-${index}`" v-model="line.characteristicCode" required />
                </Field>
                <Field>
                  <FieldLabel>Result</FieldLabel>
                  <Select v-model="line.result">
                    <SelectTrigger :aria-label="`Characteristic ${index + 1} result`">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="pass">Pass</SelectItem>
                      <SelectItem value="fail">Fail</SelectItem>
                      <SelectItem value="hold">Hold</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel :for="`measured-value-${index}`">Measured value</FieldLabel>
                  <Input :id="`measured-value-${index}`" v-model="line.measuredValue" />
                </Field>
                <div class="flex items-end justify-end">
                  <Button size="icon-sm" variant="ghost" type="button" @click="removeCharacteristicRow(index)">
                    <Trash2Icon />
                    <span class="sr-only">Remove characteristic</span>
                  </Button>
                </div>
              </div>
            </div>
          </div>

          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="record-disposition">Disposition reason</FieldLabel>
              <Input id="record-disposition" v-model="recordForm.dispositionReason" />
            </Field>
            <Field>
              <FieldLabel for="record-files">Attachment file IDs</FieldLabel>
              <Input id="record-files" v-model="recordForm.dispositionAttachmentFileIds" placeholder="file-1, file-2" />
            </Field>
          </FieldGroup>

          <div class="flex justify-end">
            <Button type="submit" :disabled="createInspectionRecordPending || !canCreateRecord">
              <Spinner v-if="createInspectionRecordPending" data-icon="inline-start" />
              <ClipboardCheckIcon v-else data-icon="inline-start" />
              Submit record
            </Button>
          </div>
        </form>
      </div>
    </section>
  </BusinessLayout>
</template>
