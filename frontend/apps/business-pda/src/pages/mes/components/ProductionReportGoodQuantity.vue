<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateItem } from '@nerv-iip/api-client'
import { NvNumberKeyboard } from '@nerv-iip/ui-mobile'
import ProductionReportSerialFields from '@/components/mes/ProductionReportSerialFields.vue'

defineProps<{
  serialRequired: boolean
  submitting: boolean
  serialPendingCount: number
  serialPending: boolean
  serialMessage: string
  serialTemplates: BusinessConsoleBarcodeTemplateItem[]
  scanGuarded: boolean
}>()

const goodQuantity = defineModel<number>('goodQuantity', { required: true })

const goodKeyboardValue = defineModel<string>('goodKeyboardValue', { required: true })

const goodKeyboardOpen = defineModel<boolean>('goodKeyboardOpen', { required: true })

const labelTemplateId = defineModel<string>('labelTemplateId', { required: true })

const emit = defineEmits<{
  refreshSerials: []
}>()
</script>

<template>
  <label class="block space-y-1">
    <span class="text-sm font-medium text-foreground">良品数</span>
    <input
      v-model.number="goodQuantity"
      data-testid="good-quantity"
      type="number"
      inputmode="numeric"
      :readonly="serialRequired"
      :disabled="submitting"
      @click="serialRequired && (goodKeyboardOpen = true)"
      min="0"
      class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
    />
  </label>

  <NvNumberKeyboard
    v-model="goodKeyboardValue"
    v-model:show="goodKeyboardOpen"
    title="良品数"
    extra-key=""
  />
  <ProductionReportSerialFields
    v-model="labelTemplateId"
    :required="serialRequired"
    :pending-count="serialPendingCount"
    :pending="serialPending"
    :message="serialMessage"
    :templates="serialTemplates"
    :disabled="submitting || scanGuarded"
    @refresh="emit('refreshSerials')"
  />
</template>
