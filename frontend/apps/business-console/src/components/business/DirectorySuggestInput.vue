<script setup lang="ts">
/**
 * 可自由录入、带目录建议的输入框：`NvCombobox` 接网关可搜目录。
 *
 * 用于「多半是已有编码、也可能是新编码」的字段（如收货批次：新到货常是新批次号）。
 * 输入的文本就是值，同时拿它去目录做服务端搜索，建议来自全部数据而不是某一页。
 */
import { NvCombobox } from '@nerv-iip/ui'
import { watch } from 'vue'
import {
  useSearchableDirectoryPicker,
  type SearchableDirectoryType,
} from '@/composables/useSearchableDirectoryPicker'

const props = defineProps<{ directoryType: SearchableDirectoryType }>()
const model = defineModel<string>({ default: '' })

// 输入的文本本身不补成一条建议：建议只列目录里真实存在的编码。
const { options, search } = useSearchableDirectoryPicker(props.directoryType, {
  selected: () => undefined,
})
watch(model, (value) => (search.value = value), { immediate: true })
</script>

<template>
  <NvCombobox v-model="model" :suggestions="options" />
</template>
