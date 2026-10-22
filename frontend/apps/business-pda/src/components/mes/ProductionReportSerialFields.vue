<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateItem } from '@nerv-iip/api-client'
import { NvMobileButton, NvPicker } from '@nerv-iip/ui-mobile'
import { computed, shallowRef } from 'vue'

const props = defineProps<{
  required: boolean
  pendingCount: number
  pending: boolean
  message: string
  templates: BusinessConsoleBarcodeTemplateItem[]
  disabled: boolean
}>()
const templateId = defineModel<string>({ required: true })
defineEmits<{ refresh: [] }>()
const open = shallowRef(false)
const options = computed(() =>
  props.templates.map((template) => ({
    value: template.templateId!,
    label: `${template.templateName} · ${template.templateCode ?? ''}`,
  })),
)
const selected = computed(
  () => options.value.find((option) => option.value === templateId.value)?.label,
)
</script>

<template>
  <section class="space-y-3 rounded-lg border border-border bg-card p-3" aria-label="单件追踪">
    <template v-if="required">
      <p class="text-base font-medium" aria-live="polite">待分配序列号：{{ pendingCount }} 个</p>
      <p class="text-sm text-muted-foreground">每件良品一个号码，提交后由系统分配。</p>
      <NvMobileButton
        v-if="pendingCount > 0"
        variant="outline"
        size="lg"
        block
        :disabled="disabled || pending"
        aria-label="选择标签模板"
        @click="open = true"
        >{{ selected || '选择标签模板' }}</NvMobileButton
      >
      <NvPicker v-model="templateId" v-model:open="open" title="选择标签模板" :options="options" />
    </template>
    <p v-else-if="!message" class="text-sm text-muted-foreground">本产品报工时不分配序列号。</p>
    <p v-if="message" role="status" class="text-sm text-muted-foreground">{{ message }}</p>
    <NvMobileButton
      v-if="message && !pending"
      variant="text"
      size="lg"
      block
      :disabled="disabled"
      @click="$emit('refresh')"
      >重新核对产品与模板</NvMobileButton
    >
  </section>
</template>
