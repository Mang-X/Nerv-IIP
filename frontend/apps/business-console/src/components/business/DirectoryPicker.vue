<script setup lang="ts">
/**
 * 目录选择器：`NvEntityPicker` 接业务目录，只能从目录里选、不接受自由文本。
 * `v-model` 回传目录项的人读编码，与过去手填提交的值同口径。
 *
 * - 工作中心 / 工位 / 物料 / 设备 / 车间 / 批次 / 序列号走网关可搜目录（服务端搜索）；
 * - 班次、产线、工厂不在可搜目录里，取基础数据资源列表，由选择器自带的本地过滤搜索；
 * - 传了 `parent`（表单里的层级字段按已选上级收窄）也取资源列表，见 `useMasterDataListPicker`。
 *
 * `creatable`：表单里的选择器可以就地新增（筛选区不开）。该类型在 `directoryCreators.ts`
 * 注册了新增弹窗、且当前用户有对应新增权限时才出现入口；建好后自动选中新建项。
 */
import { NvEntityPicker } from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'
import {
  useMasterDataListPicker,
  useSearchableDirectoryPicker,
  type DirectoryParent,
} from '@/composables/useSearchableDirectoryPicker'
import { useAuthStore } from '@/stores/auth'
import {
  directoryCreatorFor,
  type DirectoryCreateContext,
  type DirectoryCreatedItem,
} from './directoryCreators'

type ListType = 'shift' | 'production-line' | 'site'
type SearchableType =
  | 'work-center'
  | 'station'
  | 'material'
  | 'equipment'
  | 'workshop'
  | 'batch'
  | 'serial'

const DIRECTORY_NOUN: Record<SearchableType | ListType, string> = {
  'work-center': '工作中心',
  station: '工位',
  material: '物料',
  equipment: '设备',
  workshop: '车间',
  batch: '批次',
  serial: '序列号',
  shift: '班次',
  'production-line': '产线',
  site: '工厂',
}

// 其余 `NvEntityPicker` 属性（id / placeholder / clearable / disabled / invalid / aria-label /
// aria-describedby / class）透传给选择器（模板里放在最后，调用方给的值覆盖这里的默认文案）。
// 新增弹窗与选择器并列成两个根节点，所以关掉自动透传、显式绑定。
defineOptions({ inheritAttrs: false })
const props = defineProps<{
  directoryType: SearchableType | ListType
  /** 批次 / 序列号按物料收窄。 */
  skuCode?: string
  /** 允许就地新增（只在表单里开，筛选区不开）。 */
  creatable?: boolean
  /** 传给新增弹窗的上下文，用来预填父级（如已选的产线）。 */
  createContext?: DirectoryCreateContext
  /** 层级字段按已选上级收窄候选（如工位只列所选产线下的）；只对层级类型有效。 */
  parent?: DirectoryParent
}>()
const model = defineModel<string>({ default: '' })

const type = props.directoryType
const noun = DIRECTORY_NOUN[type]
const source =
  isListType(type) || (props.parent && isHierarchyType(type))
    ? useMasterDataListPicker(type, () => props.parent)
    : useSearchableDirectoryPicker(type, {
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
const showCode = type !== 'batch' && type !== 'serial'

const auth = useAuthStore()
const creator = props.creatable ? directoryCreatorFor(type) : undefined
const canCreate = computed(
  () => !!creator && (auth.principal?.permissionCodes ?? []).includes(creator.permission),
)
const createOpen = shallowRef(false)
// 每次点入口递增，作弹窗的 key：每次打开都是全新实例，按当次的 context 预填、表单从空白开始。
// 0 表示还没点过，弹窗不挂载（异步组件首次点入口时才加载，之后有缓存）。
const createSession = shallowRef(0)

function openCreate() {
  createSession.value += 1
  createOpen.value = true
}

function onCreated(item: DirectoryCreatedItem) {
  source.remember({ value: item.code, label: item.name })
  model.value = item.code
}

function isListType(type: SearchableType | ListType): type is ListType {
  return type === 'shift' || type === 'production-line' || type === 'site'
}

/** 可搜目录里能按上级收窄的层级类型（产线本来就取资源列表）。 */
function isHierarchyType(type: SearchableType): type is 'workshop' | 'work-center' | 'station' {
  return type === 'workshop' || type === 'work-center' || type === 'station'
}
</script>

<template>
  <NvEntityPicker
    v-model="model"
    :search="search"
    :options="options"
    :title="`选择${noun}`"
    :placeholder="`选择${noun}`"
    :search-placeholder="`搜索${noun}名称 / 编码…`"
    :empty-text="`没有匹配的${noun}`"
    :loading="pending"
    :server-search="serverSearch"
    :total-count="total"
    :show-code="showCode"
    :aria-label="noun"
    :create-text="canCreate ? `新增${noun}` : undefined"
    @update:search="updateSearch"
    v-bind="$attrs"
    @create="openCreate"
  />
  <component
    :is="creator.dialog"
    v-if="creator && createSession"
    :key="createSession"
    v-model:open="createOpen"
    :context="createContext"
    @created="onCreated"
  />
</template>
