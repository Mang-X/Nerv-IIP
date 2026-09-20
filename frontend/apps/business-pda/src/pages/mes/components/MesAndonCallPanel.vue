<script setup lang="ts">
import type {
  BusinessConsoleMesAndonCategory,
  BusinessConsoleMesAndonCallResponse,
} from '@nerv-iip/api-client'
import { NvMobileButton } from '@nerv-iip/ui-mobile'
import { formatOperationDateTime } from './operationPresentation'

defineProps<{
  category: BusinessConsoleMesAndonCategory | null
  pending: boolean
  unresolved: boolean
  canSubmit: boolean
  scopeMessage: string
  message: string
  receipt: BusinessConsoleMesAndonCallResponse | null
}>()
const emit = defineEmits<{
  selectCategory: [category: BusinessConsoleMesAndonCategory]
  submit: []
  reset: []
}>()
const categories: Array<{ value: BusinessConsoleMesAndonCategory; label: string }> = [
  { value: 'materialShortage', label: '缺料呼叫' },
  { value: 'equipment', label: '设备呼叫' },
  { value: 'quality', label: '质量呼叫' },
  { value: 'process', label: '工艺呼叫' },
]
</script>

<template>
  <section aria-label="异常呼叫" class="space-y-3 rounded-lg border border-border p-3">
    <h2 class="font-semibold text-foreground">异常呼叫</h2>
    <p class="text-sm text-muted-foreground">选择异常类型，请求协助处理当前工序。</p>
    <template v-if="receipt">
      <div role="status" class="space-y-1 text-sm text-foreground">
        <p>呼叫已确认</p>
        <p class="break-all">呼叫凭据：{{ receipt.id }}</p>
        <p>发起时间：{{ formatOperationDateTime(receipt.raisedAtUtc) }}</p>
      </div>
      <NvMobileButton
        type="button"
        variant="outline"
        block
        class="min-h-touch"
        @click="emit('reset')"
      >
        发起另一笔呼叫
      </NvMobileButton>
    </template>
    <template v-else>
      <div class="grid grid-cols-2 gap-2">
        <NvMobileButton
          v-for="item in categories"
          :key="item.value"
          type="button"
          :variant="category === item.value ? 'primary' : 'outline'"
          :aria-pressed="category === item.value"
          :disabled="unresolved || !!scopeMessage"
          class="min-h-touch"
          @click="emit('selectCategory', item.value)"
        >
          {{ item.label }}
        </NvMobileButton>
      </div>
      <p v-if="scopeMessage" role="alert" class="text-sm text-muted-foreground">
        {{ scopeMessage }}
      </p>
      <p v-if="message" role="alert" class="text-sm text-destructive">{{ message }}</p>
      <NvMobileButton
        v-if="category"
        type="button"
        variant="primary"
        block
        :disabled="!canSubmit"
        :loading="pending"
        class="min-h-touch"
        @click="emit('submit')"
      >
        {{ pending ? '正在呼叫…' : message || unresolved ? '重试原呼叫' : '发起呼叫' }}
      </NvMobileButton>
    </template>
  </section>
</template>
