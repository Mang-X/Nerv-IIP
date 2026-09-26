<script setup lang="ts">
/**
 * 某一类来源单据的选择器：服务端按单号搜索（维修工单列表不支持关键字，取一批本地过滤）。
 * 类型在一次挂载内固定，换类型由 `SourceDocumentPicker` 按类型重建本组件。
 */
import { NvEntityPicker } from '@nerv-iip/ui'
import { watch } from 'vue'
import {
  useSourceDocumentCatalog,
  type SourceDocumentKind,
} from '@/composables/useSourceDocumentCatalog'

const props = defineProps<{ kind: SourceDocumentKind; invalid?: boolean }>()
const model = defineModel<string>({ default: '' })

const { spec, search, options, pending, total } = useSourceDocumentCatalog(props.kind, model)
// 选定后清掉搜索词，下次打开从完整候选开始。
watch(model, () => {
  search.value = ''
})
</script>

<template>
  <NvEntityPicker
    v-model="model"
    v-model:search="search"
    :options="options"
    :title="`选择${spec.noun}`"
    :placeholder="`选择${spec.noun}`"
    :search-placeholder="spec.searchPlaceholder"
    :source-text="spec.sourceText"
    :empty-text="`没有匹配的${spec.noun}`"
    :loading="pending"
    :server-search="spec.serverSearch"
    :total-count="spec.serverSearch ? total : undefined"
    :show-code="false"
    :invalid="invalid"
    :aria-label="spec.noun"
    clearable
  />
</template>
