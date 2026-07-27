<script setup lang="ts">
/**
 * 客户 / 供应商单元格：主行显中文名称，编号降为次要信息。
 * 名录里查不到名称时只显编号（不编造名字）。
 */
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'

const props = defineProps<{ code?: string | null; fallback?: string }>()

const { resolvePartner } = useBusinessPartnerNames()
</script>

<template>
  <span v-if="!props.code" class="text-muted-foreground">{{ props.fallback ?? '未指定' }}</span>
  <span v-else-if="!resolvePartner(props.code)">{{ props.code }}</span>
  <span v-else class="grid leading-tight">
    <span>{{ resolvePartner(props.code) }}</span>
    <span class="text-xs text-muted-foreground">{{ props.code }}</span>
  </span>
</template>
