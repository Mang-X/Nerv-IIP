<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldLabel,
  NvInput,
} from '@nerv-iip/ui'
import { computed, ref, shallowRef, useId } from 'vue'
import {
  purchaseReceiptConfirmationErrors,
  readPurchaseReceiptIntent,
  submitPurchaseReceipt,
  type WmsReceiptConfirmationScope,
} from '@/composables/wms/useWmsReceiptConfirmation'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  order: BusinessConsoleWmsInboundOrderItem
  scope: WmsReceiptConfirmationScope
}>()
const emit = defineEmits<{ close: []; refresh: [] }>()
const id = useId()
const orderId = props.order.inboundOrderId!
const lines = props.order.purchaseReceipt?.lines ?? []
const original = shallowRef(readPurchaseReceiptIntent(props.scope, orderId))
const quantities = ref(
  lines.map(({ lineNo }) => {
    const line = original.value?.lines?.find((entry) => entry.lineNo === lineNo)
    return line ? String(line.receivedQuantity) : ''
  }),
)
const exchangeRate = shallowRef(original.value ? String(original.value.exchangeRate) : '')
const submitted = shallowRef(false)
const sending = shallowRef(false)
const errors = computed(() =>
  purchaseReceiptConfirmationErrors(lines, quantities.value, exchangeRate.value),
)

async function submit() {
  if (sending.value) return
  submitted.value = true
  if (!original.value && (errors.value.exchangeRate || errors.value.lines.some(Boolean))) return

  const scope = props.scope
  sending.value = true
  try {
    await submitPurchaseReceipt(scope, orderId, {
      expectedVersion: props.order.version!,
      exchangeRate: Number(exchangeRate.value),
      lines: lines.map(({ lineNo }, index) => ({
        lineNo,
        receivedQuantity: Number(quantities.value[index]),
      })),
    })
    notifySuccess('ERP 已受理实收')
    emit('close')
  } catch (error) {
    original.value = readPurchaseReceiptIntent(scope, orderId)
    notifyOperationFailure('实收确认未完成', error, '请查阅入库单状态，或重试原提交。')
  } finally {
    sending.value = false
    emit('refresh')
  }
}
</script>

<template>
  <NvDialog :open="true" @update:open="(open) => !open && !sending && emit('close')">
    <NvDialogContent class="max-h-[85dvh] overflow-y-auto">
      <NvDialogHeader>
        <NvDialogTitle>确认采购实收</NvDialogTitle>
        <NvDialogDescription>
          {{ order.inboundOrderNo }} · {{ order.siteCode }}
          <span class="block">
            {{ original ? '将按原数量和汇率重试。' : '逐行填写实收数量，当前仅支持整单实收。' }}
          </span>
        </NvDialogDescription>
      </NvDialogHeader>

      <form class="space-y-4" novalidate @submit.prevent="submit">
        <NvField v-for="(line, index) in lines" :key="line.lineNo">
          <NvFieldLabel :for="`${id}-quantity-${index}`">
            {{ line.lineNo }} 行 · {{ line.skuCode }} · 实收数量
          </NvFieldLabel>
          <p :id="`${id}-expected-${index}`" class="text-sm text-muted-foreground">
            待收 {{ line.expectedQuantity }} {{ line.uomCode }}
          </p>
          <NvInput
            :id="`${id}-quantity-${index}`"
            v-model="quantities[index]"
            inputmode="decimal"
            required
            :disabled="sending || !!original"
            :aria-invalid="submitted && !!errors.lines[index]"
            :aria-describedby="`${id}-expected-${index}${submitted && errors.lines[index] ? ` ${id}-error-${index}` : ''}`"
          />
          <NvFieldError v-if="submitted && errors.lines[index]" :id="`${id}-error-${index}`">
            {{ errors.lines[index] }}
          </NvFieldError>
        </NvField>

        <NvField>
          <NvFieldLabel :for="`${id}-exchange-rate`">汇率</NvFieldLabel>
          <NvInput
            :id="`${id}-exchange-rate`"
            v-model="exchangeRate"
            inputmode="decimal"
            required
            :disabled="sending || !!original"
            :aria-invalid="submitted && !!errors.exchangeRate"
            :aria-describedby="submitted && errors.exchangeRate ? `${id}-rate-error` : undefined"
          />
          <NvFieldError v-if="submitted && errors.exchangeRate" :id="`${id}-rate-error`">
            {{ errors.exchangeRate }}
          </NvFieldError>
        </NvField>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" :disabled="sending" @click="emit('close')">
            关闭
          </NvButton>
          <NvButton type="submit" :disabled="sending">
            {{ sending ? '提交中…' : original ? '重试原提交' : '提交实收' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
