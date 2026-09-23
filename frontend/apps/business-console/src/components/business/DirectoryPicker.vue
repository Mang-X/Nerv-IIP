<script setup lang="ts">
/**
 * 目录选择器：`NvEntityPicker` 接业务目录，只能从目录里选、不接受自由文本。
 * `v-model` 回传目录项的人读编码，与过去手填提交的值同口径。
 *
 * - 工作中心 / 物料 / 设备 / 车间 / 批次 / 序列号走网关可搜目录（服务端搜索）；
 * - 班次、产线不在可搜目录里，取基础数据资源列表在本地按搜索词过滤。
 * 两种来源返回同一形状，选择器统一按「调用方持有搜索词」的模式接。
 */
import { NvEntityPicker } from '@nerv-iip/ui'
import { watch } from 'vue'
import {
  useMasterDataListPicker,
  useSearchableDirectoryPicker,
} from '@/composables/useSearchableDirectoryPicker'

type ListType = 'shift' | 'production-line'
type SearchableType = 'work-center' | 'material' | 'equipment' | 'workshop' | 'batch' | 'serial'

const DIRECTORY_TEXT: Record<SearchableType | ListType, { noun: string; source: string }> = {
  'work-center': { noun: '工作中心', source: '数据来自基础数据工作中心' },
  material: { noun: '物料', source: '数据来自基础数据物料主数据' },
  equipment: { noun: '设备', source: '数据来自基础数据设备台账' },
  workshop: { noun: '车间', source: '数据来自基础数据车间' },
  batch: { noun: '批次', source: '数据来自库存中有在库量的批次' },
  serial: { noun: '序列号', source: '数据来自库存中有在库量的序列号' },
  shift: { noun: '班次', source: '数据来自基础数据班次' },
  'production-line': { noun: '产线', source: '数据来自基础数据产线' },
}

// 其余 `NvEntityPicker` 属性（id / placeholder / clearable / disabled / invalid / aria-label / class）
// 直接透传到根组件，调用方给的值覆盖这里的默认文案。
const props = defineProps<{
  directoryType: SearchableType | ListType
  /** 批次 / 序列号按物料收窄。 */
  skuCode?: string
}>()
const model = defineModel<string>({ default: '' })

const text = DIRECTORY_TEXT[props.directoryType]
const { search, options, pending, total } = isListType(props.directoryType)
  ? useMasterDataListPicker(props.directoryType, model)
  : useSearchableDirectoryPicker(props.directoryType, {
      selected: model,
      skuCode: () => props.skuCode,
    })
// 搜索词由这里持有，面板关闭不会清空它；选定后清掉，下次打开从完整候选开始。
watch(model, () => {
  search.value = ''
})
// 批次 / 序列号目录的名称就是「编码 · 物料」，再印一行编码是重复。
const showCode = props.directoryType !== 'batch' && props.directoryType !== 'serial'

function isListType(type: SearchableType | ListType): type is ListType {
  return type === 'shift' || type === 'production-line'
}
</script>

<template>
  <NvEntityPicker
    v-model="model"
    v-model:search="search"
    :options="options"
    :title="`选择${text.noun}`"
    :placeholder="`选择${text.noun}`"
    :search-placeholder="`搜索${text.noun}名称 / 编码…`"
    :source-text="text.source"
    :empty-text="`没有匹配的${text.noun}`"
    :loading="pending"
    server-search
    :total-count="total"
    :show-code="showCode"
    :aria-label="text.noun"
  />
</template>
