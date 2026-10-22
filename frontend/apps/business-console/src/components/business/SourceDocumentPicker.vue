<script setup lang="ts">
/**
 * 来源单据字段：该类型有可搜列表时从列表里选，没有时退回自由输入单号。
 * `v-model` 回传的值与该类型下游比对的值同口径（见 `useSourceDocumentCatalog`）。
 * 页面负责把自己的单据类型码映射成 `kind`；`kind` 为空即自由输入。
 */
import { NvInput } from '@nerv-iip/ui'
import type { SourceDocumentKind } from '@/composables/useSourceDocumentCatalog'
import SourceDocumentCatalogPicker from './SourceDocumentCatalogPicker.vue'

defineProps<{ kind?: SourceDocumentKind; invalid?: boolean }>()
const model = defineModel<string>({ default: '' })
</script>

<template>
  <SourceDocumentCatalogPicker
    v-if="kind"
    :key="kind"
    v-model="model"
    :kind="kind"
    :invalid="invalid"
  />
  <NvInput v-else v-model="model" autocomplete="off" placeholder="输入单号" :invalid="invalid" />
</template>
