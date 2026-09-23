<script setup lang="ts">
/**
 * 目录选择器：`NvEntityPicker` 接业务目录，只能从目录里选、不接受自由文本。
 * `v-model` 回传目录项的人读编码，与过去手填提交的值同口径。
 *
 * - 工作中心 / 物料 / 设备 / 车间 / 批次 / 序列号走网关可搜目录（服务端搜索）；
 * - 班次、产线不在可搜目录里，取基础数据资源列表，由选择器自带的本地过滤搜索。
 *
 * `creatable`：表单里的选择器可以就地新增（筛选区不开）。该类型在 `directoryCreators.ts`
 * 注册了新增弹窗、且当前用户有对应新增权限时才出现入口；建好后自动选中新建项。
 */
import { NvEntityPicker } from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'
import {
  useMasterDataListPicker,
  useSearchableDirectoryPicker,
} from '@/composables/useSearchableDirectoryPicker'
import { useAuthStore } from '@/stores/auth'
import {
  directoryCreatorFor,
  type DirectoryCreateContext,
  type DirectoryCreatedItem,
} from './directoryCreators'

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
// 透传给选择器（模板里放在最后，调用方给的值覆盖这里的默认文案）。新增弹窗与选择器并列成两个
// 根节点，所以关掉自动透传、显式绑定。
defineOptions({ inheritAttrs: false })
const props = defineProps<{
  directoryType: SearchableType | ListType
  /** 批次 / 序列号按物料收窄。 */
  skuCode?: string
  /** 允许就地新增（只在表单里开，筛选区不开）。 */
  creatable?: boolean
  /** 传给新增弹窗的上下文，用来预填父级（如已选的产线）。 */
  createContext?: DirectoryCreateContext
}>()
const model = defineModel<string>({ default: '' })

const text = DIRECTORY_TEXT[props.directoryType]
const source = isListType(props.directoryType)
  ? useMasterDataListPicker(props.directoryType)
  : useSearchableDirectoryPicker(props.directoryType, {
      selected: model,
      skuCode: () => props.skuCode,
    })
const { options, pending } = source
const serverSearch = source.serverSearch
const search = computed(() => (source.serverSearch ? source.search.value : undefined))
const total = computed(() => (source.serverSearch ? source.total.value : undefined))
function updateSearch(value: string) {
  if (source.serverSearch) source.search.value = value
}
// 服务端搜索的搜索词由这里持有，面板关闭不会清空它；选定后清掉，下次打开从完整候选开始。
// 本地过滤时面板每次打开重新挂载，搜索词自然重置。
if (source.serverSearch) {
  watch(model, () => updateSearch(''))
}
// 批次 / 序列号目录的名称就是「编码 · 物料」，再印一行编码是重复。
const showCode = props.directoryType !== 'batch' && props.directoryType !== 'serial'

const auth = useAuthStore()
const creator = props.creatable ? directoryCreatorFor(props.directoryType) : undefined
const canCreate = computed(
  () => !!creator && (auth.principal?.permissionCodes ?? []).includes(creator.permission),
)
const createOpen = shallowRef(false)
// 弹窗首次点入口时才挂载（异步组件随之加载），之后留着，关开只切 open。
const createMounted = shallowRef(false)

function openCreate() {
  createMounted.value = true
  createOpen.value = true
}

function onCreated(item: DirectoryCreatedItem) {
  source.remember({ value: item.code, label: item.name })
  model.value = item.code
}

function isListType(type: SearchableType | ListType): type is ListType {
  return type === 'shift' || type === 'production-line'
}
</script>

<template>
  <NvEntityPicker
    v-model="model"
    :search="search"
    :options="options"
    :title="`选择${text.noun}`"
    :placeholder="`选择${text.noun}`"
    :search-placeholder="`搜索${text.noun}名称 / 编码…`"
    :source-text="text.source"
    :empty-text="`没有匹配的${text.noun}`"
    :loading="pending"
    :server-search="serverSearch"
    :total-count="total"
    :show-code="showCode"
    :aria-label="text.noun"
    :create-text="canCreate ? `新增${text.noun}` : undefined"
    @update:search="updateSearch"
    v-bind="$attrs"
    @create="openCreate"
  />
  <component
    :is="creator.dialog"
    v-if="creator && createMounted"
    v-model:open="createOpen"
    :context="createContext"
    @created="onCreated"
  />
</template>
