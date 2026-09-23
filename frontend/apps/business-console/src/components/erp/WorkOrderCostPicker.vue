<script setup lang="ts">
/**
 * 财务页工单选择器：候选取自 ERP 工单成本（有成本记录的工单），服务端按工单号 / 物料搜索。
 * `v-model` 回传工单号，与过去手填提交的值同口径。
 */
import { NvEntityPicker } from '@nerv-iip/ui'
import { watch } from 'vue'
import { useErpWorkOrderCostPicker } from '@/composables/useBusinessErp'

const model = defineModel<string>({ default: '' })
const { search, options, pending, total } = useErpWorkOrderCostPicker(model)
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
    title="选择工单"
    placeholder="选择工单"
    search-placeholder="搜索工单号 / 物料…"
    source-text="数据来自财务已归集成本的工单"
    empty-text="没有匹配的工单"
    :loading="pending"
    server-search
    :total-count="total"
    :show-code="false"
    aria-label="工单"
  />
</template>
