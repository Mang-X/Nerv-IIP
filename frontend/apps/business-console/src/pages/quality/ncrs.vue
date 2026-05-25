<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type {
  BusinessConsoleNcrCloseRequest,
  BusinessConsoleNcrDispositionRequest,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
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
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.ncrs',
  },
})

const {
  closeNcr,
  closeNcrError,
  closeNcrPending,
  filters,
  ncrs,
  ncrsError,
  ncrsPending,
  refreshNcrs,
  submitDisposition,
  submitDispositionError,
  submitDispositionPending,
} = useQualityNcrs()

const selectedNcr = shallowRef<BusinessConsoleQualityItem>()
const detailOpen = shallowRef(false)
const dispositionSuccess = shallowRef('')
const closeSuccess = shallowRef('')

const dispositionForm = reactive({
  dispositionType: 'use-as-is',
  dispositionApprovalChainId: '',
  attachmentFileIds: '',
})

const closeForm = reactive({
  reworkWorkOrderId: '',
  scrapMovementId: '',
  returnDocumentId: '',
})

const listErrorMessage = computed(() => formatError(ncrsError.value))
const dispositionErrorMessage = computed(() => formatError(submitDispositionError.value))
const closeErrorMessage = computed(() => formatError(closeNcrError.value))
const selectedNcrId = computed(() => selectedNcr.value?.id ?? '')
const canSubmitDisposition = computed(
  () => isNonEmpty(selectedNcrId.value) && isNonEmpty(dispositionForm.dispositionType),
)
const canCloseNcr = computed(() => isNonEmpty(selectedNcrId.value))

function openNcr(ncr: BusinessConsoleQualityItem) {
  selectedNcr.value = ncr
  dispositionSuccess.value = ''
  closeSuccess.value = ''
  detailOpen.value = true
}

async function submitNcrDisposition() {
  if (!canSubmitDisposition.value) return

  const body: BusinessConsoleNcrDispositionRequest = {
    dispositionType: dispositionForm.dispositionType.trim(),
    dispositionApprovalChainId: optionalText(dispositionForm.dispositionApprovalChainId),
    attachmentFileIds: splitCsv(dispositionForm.attachmentFileIds),
  }

  await submitDisposition(selectedNcrId.value, body)
  dispositionSuccess.value = `Disposition for ${selectedNcr.value?.code ?? selectedNcrId.value} submitted.`
}

async function submitCloseNcr() {
  if (!canCloseNcr.value) return

  const body: BusinessConsoleNcrCloseRequest = {
    reworkWorkOrderId: optionalText(closeForm.reworkWorkOrderId),
    scrapMovementId: optionalText(closeForm.scrapMovementId),
    returnDocumentId: optionalText(closeForm.returnDocumentId),
  }

  await closeNcr(selectedNcrId.value, body)
  closeSuccess.value = `NCR ${selectedNcr.value?.code ?? selectedNcrId.value} close submitted.`
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

function rowKey(item: BusinessConsoleQualityItem, index: number) {
  return `${item.id ?? item.code ?? 'ncr'}:${index}`
}

function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [
    item.sourceType,
    item.sourceDocumentId,
    item.skuCode,
    item.defectQuantity === undefined || item.defectQuantity === null ? undefined : String(item.defectQuantity),
    item.defectReason,
    item.batchNo,
    item.serialNo,
  ].filter(isPresent)

  return values.length ? values.join(' / ') : 'n/a'
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? 'Request failed.' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}

function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="Quality"
        title="NCRs"
        summary="Review nonconformance reports and submit disposition or close actions through Quality."
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="ncrsPending" @click="refreshNcrs">
            <RefreshCwIcon data-icon="inline-start" />
            Refresh
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="ncr-org">Organization</FieldLabel>
            <Input id="ncr-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="ncr-env">Environment</FieldLabel>
            <Input id="ncr-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="ncr-status">Status</FieldLabel>
            <Input id="ncr-status" v-model="filters.status" placeholder="optional" />
          </Field>
          <Field>
            <FieldLabel for="ncr-take">Take</FieldLabel>
            <Input id="ncr-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="listErrorMessage" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">Nonconformance reports</h2>
          <span class="text-sm text-muted-foreground">{{ ncrs.length }} returned</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>NCR</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Summary</TableHead>
                <TableHead class="text-right">Action</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(ncr, index) in ncrs" :key="rowKey(ncr, index)">
                <TableCell>
                  <div class="flex flex-col gap-0.5">
                    <span class="font-medium">{{ ncr.code ?? 'n/a' }}</span>
                    <span class="text-xs text-muted-foreground">{{ ncr.id ?? 'No NCR id' }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ ncr.status ?? 'unknown' }}</Badge>
                </TableCell>
                <TableCell>{{ qualityItemSummary(ncr) }}</TableCell>
                <TableCell class="text-right">
                  <Button size="sm" variant="outline" type="button" @click="openNcr(ncr)">
                    Open
                  </Button>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!ncrs.length && !ncrsPending" :colspan="4">
                No NCRs returned.
              </TableEmpty>
              <TableEmpty v-if="ncrsPending" :colspan="4">Loading NCRs...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <Sheet v-model:open="detailOpen">
        <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
          <SheetHeader>
            <SheetTitle>{{ selectedNcr?.code ?? 'NCR detail' }}</SheetTitle>
            <SheetDescription>
              {{ selectedNcr ? qualityItemSummary(selectedNcr) : 'Review and submit quality actions.' }}
            </SheetDescription>
          </SheetHeader>

          <div class="grid gap-4 px-1">
            <div class="grid gap-2 rounded-lg border p-3">
              <div class="flex items-center justify-between gap-2">
                <span class="text-sm font-medium text-foreground">Status</span>
                <Badge variant="secondary">{{ selectedNcr?.status ?? 'unknown' }}</Badge>
              </div>
              <div class="grid gap-1 text-sm text-muted-foreground">
                <span>ID: {{ selectedNcr?.id ?? 'n/a' }}</span>
                <span>Code: {{ selectedNcr?.code ?? 'n/a' }}</span>
              </div>
            </div>

            <form class="grid gap-3 rounded-lg border p-3" @submit.prevent="submitNcrDisposition">
              <div>
                <p class="text-xs font-bold uppercase text-primary">Disposition</p>
                <h2 class="text-base font-semibold text-foreground">Submit disposition</h2>
              </div>
              <BusinessFormStatus :error="dispositionErrorMessage" :success="dispositionSuccess" />
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel>Disposition type</FieldLabel>
                  <Select v-model="dispositionForm.dispositionType">
                    <SelectTrigger aria-label="Disposition type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="use-as-is">Use as is</SelectItem>
                      <SelectItem value="rework">Rework</SelectItem>
                      <SelectItem value="scrap">Scrap</SelectItem>
                      <SelectItem value="return-to-supplier">Return to supplier</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="ncr-approval-chain">Approval chain</FieldLabel>
                  <Input id="ncr-approval-chain" v-model="dispositionForm.dispositionApprovalChainId" />
                </Field>
                <Field>
                  <FieldLabel for="ncr-disposition-files">Attachment file IDs</FieldLabel>
                  <Input id="ncr-disposition-files" v-model="dispositionForm.attachmentFileIds" placeholder="file-1, file-2" />
                </Field>
              </FieldGroup>
              <div class="flex justify-end">
                <Button type="submit" :disabled="submitDispositionPending || !canSubmitDisposition">
                  <Spinner v-if="submitDispositionPending" data-icon="inline-start" />
                  <SendIcon v-else data-icon="inline-start" />
                  Submit disposition
                </Button>
              </div>
            </form>

            <form class="grid gap-3 rounded-lg border p-3" @submit.prevent>
              <div>
                <p class="text-xs font-bold uppercase text-primary">Close</p>
                <h2 class="text-base font-semibold text-foreground">Close NCR</h2>
              </div>
              <BusinessFormStatus :error="closeErrorMessage" :success="closeSuccess" />
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel for="ncr-rework">Rework work order</FieldLabel>
                  <Input id="ncr-rework" v-model="closeForm.reworkWorkOrderId" />
                </Field>
                <Field>
                  <FieldLabel for="ncr-scrap">Scrap movement</FieldLabel>
                  <Input id="ncr-scrap" v-model="closeForm.scrapMovementId" />
                </Field>
                <Field>
                  <FieldLabel for="ncr-return">Return document</FieldLabel>
                  <Input id="ncr-return" v-model="closeForm.returnDocumentId" />
                </Field>
              </FieldGroup>

              <SheetFooter>
                <AlertDialog>
                  <AlertDialogTrigger as-child>
                    <Button
                      type="button"
                      variant="destructive"
                      :disabled="closeNcrPending || !canCloseNcr"
                    >
                      <Spinner v-if="closeNcrPending" data-icon="inline-start" />
                      <CheckCircle2Icon v-else data-icon="inline-start" />
                      Close NCR
                    </Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Close this NCR?</AlertDialogTitle>
                      <AlertDialogDescription>
                        This submits a Quality close action only. Inventory, WMS, and MES follow their own service workflows.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction @click="submitCloseNcr">Confirm close</AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
              </SheetFooter>
            </form>
          </div>
        </SheetContent>
      </Sheet>
    </section>
  </BusinessLayout>
</template>
