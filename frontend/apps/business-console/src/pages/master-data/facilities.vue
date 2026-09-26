<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DirectoryCreateContext } from '@/components/business/directoryCreators'
import type { MasterDataTreeNodeData } from '@/components/masterData/MasterDataTreeNode.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import MasterDataTreeNode from '@/components/masterData/MasterDataTreeNode.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import ProductionLineCreateDialog from '@/components/masterData/ProductionLineCreateDialog.vue'
import SiteCreateDialog from '@/components/masterData/SiteCreateDialog.vue'
import StationCreateDialog from '@/components/masterData/StationCreateDialog.vue'
import WorkCenterCreateDialog from '@/components/masterData/WorkCenterCreateDialog.vue'
import WorkshopCreateDialog from '@/components/masterData/WorkshopCreateDialog.vue'
import { useIncludeDisabledFilter } from '@/composables/masterDataIncludeDisabled'
import { useMasterDataLifecycleConfirm } from '@/composables/masterDataLifecycleConfirm'
import {
  useBusinessWorkshops,
  useMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import { DEFAULT_TIME_ZONE, TIME_ZONE_OPTIONS } from '@/data/timeZones'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  ScrollArea,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { FactoryIcon, PlusIcon, RefreshCwIcon, SearchIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch, type Component } from 'vue'
import { RouterLink } from 'vue-router'
import { formatDateTime } from '@/utils/format'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '工厂结构',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

// 列表端点有分页上限（默认 take=100），层级树需尽量全量拼装：用较大 take 兜底。
// 真正全量需后端全量端点（#373），超过此阈值的层级在树底给出提示。
const TREE_TAKE = 200
const DEFAULT_TIMEZONE = DEFAULT_TIME_ZONE
const WORK_CENTER_DEFAULTS = {
  capacityUnit: 'minutes',
  capacityMinutesPerDay: 480,
  finiteCapacity: true,
}

const sites = useMasterDataResource('site')
const workshops = useBusinessWorkshops()
const lines = useMasterDataResource('production-line')
const workCenters = useMasterDataResource('work-center')
const stations = useMasterDataResource('station')
const siteActions = useMasterDataResourceActions('site')
const workshopActions = useMasterDataResourceActions('workshop')
const lineActions = useMasterDataResourceActions('production-line')
const wcActions = useMasterDataResourceActions('work-center')
const stationActions = useMasterDataResourceActions('station')
// 停用/启用确认框收在页面层单实例，行操作只负责指向当前行（#1591）。
const lifecycle = useMasterDataLifecycleConfirm()

// 拉大每类的 take，尽量一页拼全树（不做假分页，超上限处给提示）。
for (const r of [sites, lines, workCenters, stations]) r.filters.take = TREE_TAKE
// 停用后的节点默认不在树里，「启用」入口就永远够不到；各层共用一个开关（#1594）。
const includeDisabled = useIncludeDisabledFilter([
  sites.filters,
  workshops.filters,
  lines.filters,
  workCenters.filters,
  stations.filters,
])
workshops.filters.take = TREE_TAKE

// 工作日历列表（仅供"默认工作日历"在详情面板解析编码→名称）。
const calendars = useMasterDataResource('work-calendar')
calendars.filters.take = TREE_TAKE

// 归属 / 日历 编码 → 名称解析（详情面板显示名称而非裸编码；列表加载后 computed 实时更新）。
const siteNameByCode = computed(
  () => new Map(sites.items.value.map((s) => [s.code ?? '', s.displayName ?? s.code ?? ''])),
)
const workshopNameByCode = computed(
  () =>
    new Map(workshops.workshops.value.map((w) => [w.code ?? '', w.displayName ?? w.code ?? ''])),
)
const lineNameByCode = computed(
  () => new Map(lines.items.value.map((l) => [l.code ?? '', l.displayName ?? l.code ?? ''])),
)
const wcNameByCode = computed(
  () => new Map(workCenters.items.value.map((w) => [w.code ?? '', w.displayName ?? w.code ?? ''])),
)
const calendarNameByCode = computed(
  () => new Map(calendars.items.value.map((c) => [c.code ?? '', c.displayName ?? c.code ?? ''])),
)
function nameOf(map: Map<string, string>, code: string): string {
  return code ? (map.get(code) ?? code) : ''
}

// 节点类型与其在层级中的角色。工位和工作中心都挂在产线下：工位是层级上的下级（工厂 ▸ 车间 ▸
// 产线 ▸ 工位），工作中心是排产与成本口径的产能单元。
type NodeType = 'site' | 'workshop' | 'production-line' | 'work-center' | 'station'
interface TreeNode {
  type: NodeType
  code: string
  displayName: string
  active: boolean
  item: BusinessConsoleResourceItem
  children: TreeNode[]
}

const NODE_LABEL: Record<NodeType, string> = {
  site: '工厂',
  workshop: '车间',
  'production-line': '产线',
  'work-center': '工作中心',
  station: '工位',
}
// 各父类型可就地新建的子级类型；树节点上的「+」建第一种。
const CHILD_TYPES: Partial<Record<NodeType, NodeType[]>> = {
  site: ['workshop'],
  workshop: ['production-line'],
  'production-line': ['work-center', 'station'],
}

function toNode(item: BusinessConsoleResourceItem, type: NodeType): TreeNode {
  return {
    type,
    code: item.code ?? '',
    displayName: item.displayName ?? item.code ?? '',
    active: item.active !== false,
    item,
    children: [],
  }
}

// ---- 按 typed code 链路前端拼树（后端无父级过滤）----
// workshop.siteCode→site；line.workshopCode||siteCode→workshop 或直接挂 site；
// workCenter.lineCode→line（plantCode→site 兜底）。
const tree = computed<TreeNode[]>(() => {
  const siteNodes = sites.items.value.map((s) => toNode(s, 'site'))
  const siteByCode = new Map(siteNodes.map((n) => [n.code, n]))

  const workshopNodes = workshops.workshops.value.map((w) => toNode(w, 'workshop'))
  const workshopByCode = new Map(workshopNodes.map((n) => [n.code, n]))
  for (const w of workshopNodes) {
    const parent = w.item.siteCode ? siteByCode.get(w.item.siteCode) : undefined
    if (parent) parent.children.push(w)
  }

  const lineNodes = lines.items.value.map((l) => toNode(l, 'production-line'))
  const lineByCode = new Map(lineNodes.map((n) => [n.code, n]))
  for (const l of lineNodes) {
    // 优先挂车间；无车间归属（小厂压扁）则直接挂工厂。
    const ws = l.item.workshopCode ? workshopByCode.get(l.item.workshopCode) : undefined
    if (ws) {
      ws.children.push(l)
      continue
    }
    const site = l.item.siteCode ? siteByCode.get(l.item.siteCode) : undefined
    if (site) site.children.push(l)
  }

  const wcNodes = workCenters.items.value.map((wc) => toNode(wc, 'work-center'))
  for (const wc of wcNodes) {
    const line = wc.item.lineCode ? lineByCode.get(wc.item.lineCode) : undefined
    if (line) {
      line.children.push(wc)
      continue
    }
    // 兜底：无产线归属时按 plantCode 直接挂工厂，避免丢节点。
    const site = wc.item.plantCode ? siteByCode.get(wc.item.plantCode) : undefined
    if (site) site.children.push(wc)
  }

  for (const station of stations.items.value) {
    lineByCode.get(station.lineCode ?? '')?.children.push(toNode(station, 'station'))
  }

  return siteNodes
})

const totalNodes = computed(
  () =>
    sites.total.value +
    workshops.workshopsTotal.value +
    lines.total.value +
    workCenters.total.value +
    stations.total.value,
)
const treePending = computed(
  () =>
    sites.pending.value ||
    workshops.workshopsPending.value ||
    lines.pending.value ||
    workCenters.pending.value ||
    stations.pending.value,
)
// 任一类命中分页上限：树可能不全，提示需后端全量端点（#373）。
const treeTruncated = computed(() =>
  [
    sites.total.value,
    workshops.workshopsTotal.value,
    lines.total.value,
    workCenters.total.value,
    stations.total.value,
  ].some((t) => t > TREE_TAKE),
)
const treeListError = computed(() => {
  const e =
    sites.error.value ??
    workshops.workshopsError.value ??
    lines.error.value ??
    workCenters.error.value ??
    stations.error.value
  return inlineErrorMessage(e, '层级数据加载失败，请刷新重试。')
})

// ---- 渲染森林：始终以工厂为根（含单一工厂——藏根会导致无法选中/编辑该工厂）。
// 小厂的"少层"靠默认全展开 + 自动选中首个工厂实现（见下方 watch），不藏根。 ----
const forest = computed<TreeNode[]>(() => tree.value)

// ---- 树搜索 + 展开/折叠 ----
const treeSearch = ref('')
const expanded = reactive(new Set<string>())
function nodeKey(node: TreeNode | MasterDataTreeNodeData) {
  return `${node.type}:${node.code}`
}
function matchesSearch(node: TreeNode, kw: string): boolean {
  if (!kw) return true
  const self = `${node.displayName} ${node.code}`.toLowerCase().includes(kw)
  return self || node.children.some((c) => matchesSearch(c, kw))
}
const filteredForest = computed<TreeNode[]>(() => {
  const kw = treeSearch.value.trim().toLowerCase()
  if (!kw) return forest.value
  const prune = (nodes: TreeNode[]): TreeNode[] =>
    nodes.filter((n) => matchesSearch(n, kw)).map((n) => ({ ...n, children: prune(n.children) }))
  return prune(forest.value)
})

// 节点少时默认全展开；命中搜索自动展开祖先；切换数据时保留选中 code。
watch(
  [forest, treeSearch],
  () => {
    if (treeSearch.value.trim()) {
      // 搜索态：展开所有命中节点的祖先（filteredForest 已剪枝，展开其全部）。
      const expandAll = (nodes: TreeNode[]) => {
        for (const n of nodes) {
          if (n.children.length) {
            expanded.add(nodeKey(n))
            expandAll(n.children)
          }
        }
      }
      expandAll(filteredForest.value)
      return
    }
    if (totalNodes.value > 0 && totalNodes.value < 50 && expanded.size === 0) {
      const expandAll = (nodes: TreeNode[]) => {
        for (const n of nodes) {
          if (n.children.length) {
            expanded.add(nodeKey(n))
            expandAll(n.children)
          }
        }
      }
      expandAll(forest.value)
    }
  },
  { immediate: true },
)

function toggleExpand(node: TreeNode | MasterDataTreeNodeData) {
  const key = nodeKey(node)
  if (expanded.has(key)) expanded.delete(key)
  else expanded.add(key)
}
const allExpanded = computed(() => {
  let hasParent = false
  const check = (nodes: TreeNode[]): boolean =>
    nodes.every((n) => {
      if (!n.children.length) return true
      hasParent = true
      return expanded.has(nodeKey(n)) && check(n.children)
    })
  const ok = check(forest.value)
  return hasParent && ok
})
function expandAllToggle() {
  if (allExpanded.value) {
    expanded.clear()
    return
  }
  const add = (nodes: TreeNode[]) => {
    for (const n of nodes) {
      if (n.children.length) {
        expanded.add(nodeKey(n))
        add(n.children)
      }
    }
  }
  add(forest.value)
}

// ---- 选中态（单选，持久 code）+ 详情 ----
const selectedKey = ref<string | null>(null)
const selectedNode = computed<TreeNode | null>(() => {
  if (!selectedKey.value) return null
  let found: TreeNode | null = null
  const walk = (nodes: TreeNode[]) => {
    for (const n of nodes) {
      if (nodeKey(n) === selectedKey.value) {
        found = n
        return
      }
      walk(n.children)
    }
  }
  // 选中可能落在被压扁的根上，故从完整 tree 找。
  walk(tree.value)
  return found
})
function selectNode(node: TreeNode | MasterDataTreeNodeData) {
  selectedKey.value = nodeKey(node)
}
// 数据就绪后默认选中首个工厂：右侧详情不空,单工厂也能直接选中/编辑该工厂。
watch(
  forest,
  (roots) => {
    if (!selectedKey.value && roots.length > 0) selectedKey.value = nodeKey(roots[0]!)
  },
  { immediate: true },
)

// 选中路径（用于面包屑：工厂 ▸ 车间 ▸ 产线 ▸ 工作中心）。
const selectedPath = computed<TreeNode[]>(() => {
  const target = selectedKey.value
  if (!target) return []
  const path: TreeNode[] = []
  const walk = (nodes: TreeNode[]): boolean => {
    for (const n of nodes) {
      path.push(n)
      if (nodeKey(n) === target) return true
      if (walk(n.children)) return true
      path.pop()
    }
    return false
  }
  walk(tree.value)
  return path
})

// 选中节点的详情字段（按类型展示 typed 字段）。
const detailLoading = shallowRef(false)
const detailExtra = shallowRef<Record<string, unknown> | null>(null)
const ACTIONS_BY_TYPE = {
  site: siteActions,
  workshop: workshopActions,
  'production-line': lineActions,
  'work-center': wcActions,
  station: stationActions,
} as const

watch(selectedNode, async (node) => {
  detailExtra.value = null
  if (!node?.code) return
  detailLoading.value = true
  try {
    detailExtra.value = (await ACTIONS_BY_TYPE[node.type].fetchDetail(node.code)) as Record<
      string,
      unknown
    > | null
  } finally {
    detailLoading.value = false
  }
})

const detailFields = computed(() => {
  const node = selectedNode.value
  if (!node) return [] as { label: string; value: string }[]
  const d = detailExtra.value ?? {}
  const item = node.item
  const pick = (k: string) => {
    const v = (d as Record<string, unknown>)[k] ?? (item as Record<string, unknown>)[k]
    return v == null || v === '' ? '' : String(v)
  }
  switch (node.type) {
    case 'site':
      return [
        { label: '工厂编码', value: node.code },
        { label: '工厂名称', value: node.displayName },
        { label: '时区', value: pick('timezone') },
        { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
      ]
    case 'workshop':
      return [
        { label: '车间编码', value: node.code },
        { label: '车间名称', value: node.displayName },
        { label: '所属工厂', value: nameOf(siteNameByCode.value, pick('siteCode')) },
      ]
    case 'production-line':
      return [
        { label: '产线编码', value: node.code },
        { label: '产线名称', value: node.displayName },
        { label: '所属工厂', value: nameOf(siteNameByCode.value, pick('siteCode')) },
        { label: '所属车间', value: nameOf(workshopNameByCode.value, pick('workshopCode')) },
        { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
      ]
    case 'work-center':
      return [
        { label: '工作中心编码', value: node.code },
        { label: '工作中心名称', value: node.displayName },
        { label: '所属工厂', value: nameOf(siteNameByCode.value, pick('plantCode')) },
        { label: '所属产线', value: nameOf(lineNameByCode.value, pick('lineCode')) },
        {
          label: '默认工作日历',
          value: nameOf(calendarNameByCode.value, pick('defaultCalendarCode')),
        },
        { label: '日产能（分钟）', value: pick('capacityMinutesPerDay') },
        { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
      ]
    case 'station':
      return [
        { label: '工位编码', value: node.code },
        { label: '工位名称', value: node.displayName },
        { label: '所属产线', value: nameOf(lineNameByCode.value, pick('lineCode')) },
        { label: '关联工作中心', value: nameOf(wcNameByCode.value, pick('workCenterCode')) },
        { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
      ]
  }
})

function isNonEmpty(value: string) {
  return value.trim().length > 0
}

function refreshAll() {
  void sites.refresh()
  void workshops.refreshWorkshops()
  void lines.refresh()
  void workCenters.refresh()
  void stations.refresh()
}

// ================= 新建（含就地建子级，父级带出为只读归属） =================
// 各类的新建弹窗与表单选择器里就地新增共用（车间 / 产线 / 工作中心 / 工位的约定见
// `directoryCreators.ts`）。每次打开递增 key 重新挂载，按当次的上级带出归属。
const CREATE_DIALOGS: Record<NodeType, Component> = {
  site: SiteCreateDialog,
  workshop: WorkshopCreateDialog,
  'production-line': ProductionLineCreateDialog,
  'work-center': WorkCenterCreateDialog,
  station: StationCreateDialog,
}
// 父节点作为子级的哪一级上级带出；上级所在工厂取自父节点自身的归属。
const PARENT_CONTEXT_KEY: Partial<Record<NodeType, string>> = {
  site: 'siteCode',
  workshop: 'workshopCode',
  'production-line': 'lineCode',
}
const createOpen = shallowRef(false)
const creating = shallowRef<{
  session: number
  type: NodeType
  bindings: { context?: DirectoryCreateContext }
}>()

function openCreate(type: NodeType, bindings: { context?: DirectoryCreateContext }) {
  creating.value = { session: (creating.value?.session ?? 0) + 1, type, bindings }
  createOpen.value = true
}
// 顶部「+ 新建工厂」建根。
function openCreateRoot() {
  openCreate('site', {})
}
// 选中父节点 → 「+ 新建<子级>」，父级带出为只读归属。
function openCreateChild(
  parent: TreeNode | MasterDataTreeNodeData,
  childType = CHILD_TYPES[parent.type as NodeType]?.[0],
) {
  if (!childType) return
  const context: DirectoryCreateContext = {
    siteCode: parent.item.siteCode ?? '',
    [PARENT_CONTEXT_KEY[parent.type as NodeType]!]: parent.code,
  }
  openCreate(childType, { context })
}

// ================= 编辑（编码只读；改名 + 改挂上级，归属经 update 透传） =================
const editOpen = ref(false)
const editType = shallowRef<NodeType>('site')
const editShowErrors = ref(false)
const editLoading = shallowRef(false)
const editCode = shallowRef('')
const editForm = reactive({
  name: '',
  timezone: DEFAULT_TIMEZONE,
  // 归属（可改挂上级）：车间→siteCode；产线→workshopCode/siteCode；工作中心→lineCode/plantCode；
  // 工位→lineCode（工作中心只是可选关联）。
  siteCode: '',
  workshopCode: '',
  plantCode: '',
  lineCode: '',
  workCenterCode: '',
  defaultCalendarCode: '',
  capacityMinutesPerDay: '480',
})
// 时区只能是合法 IANA 标识符（手输 `GMT+8` 之类会让排产/日历算错），一律从常用清单里选；
// 已存的历史值若不在清单里，兜住它显示，不至于变成未选。
function timeZoneOptionsWith(current: string) {
  const trimmed = current.trim()
  if (trimmed && !TIME_ZONE_OPTIONS.some((option) => option.value === trimmed)) {
    return [{ value: trimmed, label: trimmed }, ...TIME_ZONE_OPTIONS]
  }
  return TIME_ZONE_OPTIONS
}
const editTimeZoneOptions = computed(() => timeZoneOptionsWith(editForm.timezone))

const canEdit = computed(() => {
  if (!isNonEmpty(editForm.name)) return false
  switch (editType.value) {
    case 'workshop':
      return isNonEmpty(editForm.siteCode)
    case 'production-line':
      return isNonEmpty(editForm.siteCode)
    case 'work-center':
      return isNonEmpty(editForm.plantCode) && isNonEmpty(editForm.lineCode)
    case 'station':
      return isNonEmpty(editForm.lineCode)
    default:
      return true
  }
})
const editPending = computed(() => ACTIONS_BY_TYPE[editType.value].updatePending.value)
const editTitle = computed(() => `编辑${NODE_LABEL[editType.value]} · ${editCode.value}`)

// 改挂上级的父级候选（取页内已加载列表）。层级类型固定（工厂→车间→产线→工作中心），
// 跨层挂载结构上不可能成环；这里只把候选限定为合法上级，并随上层选择联动过滤。
const editWorkshopOptions = computed(() =>
  workshops.workshops.value.filter(
    (w) => !editForm.siteCode || (w.siteCode ?? '') === editForm.siteCode,
  ),
)
const editLineOptions = computed(() =>
  lines.items.value.filter((l) => !editForm.plantCode || (l.siteCode ?? '') === editForm.plantCode),
)
// reka 的 SelectItem 不允许空串 value。“无车间（直挂工厂）”用哨兵值表示，仅作用于下拉绑定；
// 提交仍按 空串→null 处理（见保存逻辑）。
const NONE_OPTION = '__none__'
const editWorkshopValue = computed({
  get: () => editForm.workshopCode || NONE_OPTION,
  set: (v) => {
    editForm.workshopCode = v === NONE_OPTION ? '' : v
  },
})
// 改挂工厂后，原车间 / 产线可能不再归属该工厂——置空让用户重选，避免归属错配。
const editCascadeReady = shallowRef(false)
watch(
  () => editForm.siteCode,
  () => {
    if (editType.value !== 'production-line' || !editCascadeReady.value) return
    if (
      editForm.workshopCode &&
      !editWorkshopOptions.value.some((w) => (w.code ?? '') === editForm.workshopCode)
    ) {
      editForm.workshopCode = ''
    }
  },
)
watch(
  () => editForm.plantCode,
  () => {
    if (editType.value !== 'work-center' || !editCascadeReady.value) return
    if (
      editForm.lineCode &&
      !editLineOptions.value.some((l) => (l.code ?? '') === editForm.lineCode)
    ) {
      editForm.lineCode = ''
    }
  },
)

// 工位改挂产线后，原关联的工作中心属于旧产线，清掉让用户重选。同步触发：打开编辑回填
// 归属时 editCascadeReady 还是 false，不会把刚载入的关联清掉。
watch(
  () => editForm.lineCode,
  () => {
    if (editType.value === 'station' && editCascadeReady.value) editForm.workCenterCode = ''
  },
  { flush: 'sync' },
)

watch(editOpen, (open) => {
  if (open) editShowErrors.value = false
})

async function openEdit(node: TreeNode) {
  if (!node.code) return
  editType.value = node.type
  editCode.value = node.code
  editShowErrors.value = false
  editLoading.value = true
  editOpen.value = true
  // 回填期间关闭级联清空，避免把刚载入的归属误清掉。
  editCascadeReady.value = false
  Object.assign(editForm, {
    name: node.displayName,
    timezone: DEFAULT_TIMEZONE,
    siteCode: '',
    workshopCode: '',
    plantCode: '',
    lineCode: '',
    workCenterCode: '',
    defaultCalendarCode: '',
    capacityMinutesPerDay: '480',
  })
  try {
    const d = (await ACTIONS_BY_TYPE[node.type].fetchDetail(node.code)) as
      | Record<string, unknown>
      | undefined
    const str = (k: string) => {
      const v = d?.[k] ?? (node.item as Record<string, unknown>)[k]
      return v == null ? '' : String(v)
    }
    Object.assign(editForm, {
      name: str('name') || node.displayName,
      timezone: str('timezone') || DEFAULT_TIMEZONE,
      siteCode: str('siteCode'),
      workshopCode: str('workshopCode'),
      plantCode: str('plantCode'),
      lineCode: str('lineCode'),
      workCenterCode: str('workCenterCode'),
      defaultCalendarCode: str('defaultCalendarCode'),
      capacityMinutesPerDay: str('capacityMinutesPerDay') || '480',
    })
  } finally {
    editLoading.value = false
    editCascadeReady.value = true
  }
}

async function submitEdit() {
  if (!canEdit.value) {
    editShowErrors.value = true
    return
  }
  const name = editForm.name.trim()
  const code = editCode.value
  try {
    switch (editType.value) {
      case 'site':
        await siteActions.update(code, { name, timezone: editForm.timezone.trim() })
        break
      case 'workshop':
        await workshopActions.update(code, { name, siteCode: editForm.siteCode.trim() })
        break
      case 'production-line':
        await lineActions.update(code, {
          name,
          siteCode: editForm.siteCode.trim(),
          workshopCode: editForm.workshopCode.trim() || null,
        })
        break
      case 'work-center':
        await wcActions.update(code, {
          name,
          plantCode: editForm.plantCode.trim(),
          lineCode: editForm.lineCode.trim(),
          defaultCalendarCode: editForm.defaultCalendarCode.trim(),
          capacityMinutesPerDay:
            Number(editForm.capacityMinutesPerDay) || WORK_CENTER_DEFAULTS.capacityMinutesPerDay,
          capacityUnit: WORK_CENTER_DEFAULTS.capacityUnit,
          finiteCapacity: WORK_CENTER_DEFAULTS.finiteCapacity,
        })
        break
      case 'station':
        // 工作中心传空串表示取消关联（不传则保留原值）。
        await stationActions.update(code, {
          name,
          lineCode: editForm.lineCode.trim(),
          workCenterCode: editForm.workCenterCode,
        })
        break
    }
    notifySuccess(`${NODE_LABEL[editType.value]}「${name}」已更新。`)
    editShowErrors.value = false
    editOpen.value = false
  } catch (error) {
    notifyOperationFailure(
      `更新${NODE_LABEL[editType.value]}失败`,
      error,
      `更新${NODE_LABEL[editType.value]}失败，请稍后重试。`,
    )
  }
}

// 选中节点对应的 actions（供 RowActions 停用/启用）。
const selectedActions = computed(() =>
  selectedNode.value ? ACTIONS_BY_TYPE[selectedNode.value.type] : null,
)
/** 行操作把当前节点交给页面层单实例确认框；节点/动作缺一不可，缺了就不开框（#1591）。 */
function requestLifecycle(row: BusinessConsoleResourceItem) {
  const node = selectedNode.value
  const actions = selectedActions.value
  if (!node || !actions) return
  lifecycle.request(row, actions, NODE_LABEL[node.type])
}

// 选中节点能就地新建的子级，以及各类子级的数量。
const childTypesOfSelected = computed(() =>
  selectedNode.value ? (CHILD_TYPES[selectedNode.value.type] ?? []) : [],
)
function childCount(type: NodeType) {
  return selectedNode.value?.children.filter((child) => child.type === type).length ?? 0
}
// 通用树节点的 childLabelOf 回调（参数为开放 string）：映射到本页的子级中文名。
function childLabelOf(type: string): string | undefined {
  const child = CHILD_TYPES[type as NodeType]?.[0]
  return child ? NODE_LABEL[child] : undefined
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工厂结构"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${totalNodes} 个节点`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="treePending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreateRoot">
          <PlusIcon aria-hidden="true" />
          新建工厂
        </NvButton>
      </template>
    </NvPageHeader>

    <!-- 工厂▸车间▸产线▸工作中心是逐级包含的层级计数，不是同一总量的构成，
         所以用一条并列的 Strip，不用环形卡。 -->
    <NvMetricStrip
      :cells="[
        { key: 'site', label: '工厂', value: sites.total.value, unit: '个' },
        { key: 'workshop', label: '车间', value: workshops.workshopsTotal.value, unit: '个' },
        { key: 'line', label: '产线', value: lines.total.value, unit: '条' },
        {
          key: 'workCenter',
          label: '工作中心',
          value: workCenters.total.value,
          unit: '个',
          meta: '排产与报工的产能单元',
        },
        { key: 'station', label: '工位', value: stations.total.value, unit: '个' },
      ]"
    />

    <div class="grid gap-4 md:grid-cols-[320px_minmax(0,1fr)]">
      <!-- 左：层级树 -->
      <section
        class="flex flex-col gap-3 rounded-lg border border-border bg-card p-3"
        aria-label="工厂结构树"
      >
        <div class="relative">
          <SearchIcon
            class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <NvInput
            v-model="treeSearch"
            class="pl-8"
            placeholder="搜索工厂、车间、产线、工作中心、工位"
            aria-label="搜索树"
          />
        </div>
        <div class="flex items-center justify-between">
          <NvButton
            v-if="forest.length"
            size="sm"
            variant="ghost"
            type="button"
            class="h-7 px-2 text-xs"
            @click="expandAllToggle"
          >
            {{ allExpanded ? '全部折叠' : '全部展开' }}
          </NvButton>
          <IncludeDisabledFilter v-model="includeDisabled" class="ml-auto" />
        </div>

        <p v-if="treeListError" class="text-sm text-destructive" role="alert">
          {{ treeListError }}
        </p>

        <ScrollArea class="h-[28rem]">
          <div v-if="treePending && !forest.length" class="px-1 py-2 text-sm text-muted-foreground">
            加载层级中…
          </div>
          <!-- 整树无数据 -->
          <div v-else-if="!tree.length" class="grid gap-2 px-1 py-6 text-center">
            <FactoryIcon class="mx-auto size-8 text-muted-foreground" aria-hidden="true" />
            <p class="text-sm text-muted-foreground">还没有工厂，点击创建第一条。</p>
            <NvButton size="sm" type="button" class="mx-auto" @click="openCreateRoot">
              <PlusIcon aria-hidden="true" />
              新建工厂
            </NvButton>
          </div>
          <!-- 搜索无命中 -->
          <div
            v-else-if="treeSearch.trim() && !filteredForest.length"
            class="grid gap-2 px-1 py-6 text-center"
          >
            <p class="text-sm text-muted-foreground">没有匹配「{{ treeSearch.trim() }}」的节点。</p>
            <NvButton
              size="sm"
              variant="outline"
              type="button"
              class="mx-auto"
              @click="treeSearch = ''"
              >清空搜索</NvButton
            >
          </div>
          <ul v-else class="grid gap-0.5" role="tree">
            <MasterDataTreeNode
              v-for="node in filteredForest"
              :key="nodeKey(node)"
              :node="node"
              :depth="0"
              :expanded="expanded"
              :selected-key="selectedKey"
              :child-label-of="childLabelOf"
              @select="selectNode"
              @toggle="toggleExpand"
              @create-child="openCreateChild"
            />
          </ul>
        </ScrollArea>

        <p v-if="treeTruncated" class="text-xs text-muted-foreground">
          节点较多，当前仅展示前 {{ TREE_TAKE }} 条；请使用搜索缩小范围。
        </p>
      </section>

      <!-- 右：详情 -->
      <section class="rounded-lg border border-border bg-card p-4" aria-label="节点详情">
        <div v-if="!selectedNode" class="grid place-items-center gap-2 py-16 text-center">
          <FactoryIcon class="size-8 text-muted-foreground" aria-hidden="true" />
          <p class="text-sm text-muted-foreground">从左侧选择一个节点查看详情。</p>
        </div>
        <div v-else class="grid gap-4">
          <!-- 面包屑：工厂 ▸ 车间 ▸ 产线 ▸ 工作中心 -->
          <nav
            class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground"
            aria-label="选中路径"
          >
            <template v-for="(node, idx) in selectedPath" :key="nodeKey(node)">
              <span v-if="idx > 0" aria-hidden="true">▸</span>
              <button
                type="button"
                class="rounded px-1 hover:text-foreground hover:underline"
                :class="idx === selectedPath.length - 1 ? 'font-medium text-foreground' : ''"
                @click="selectNode(node)"
              >
                {{ node.displayName }}
              </button>
            </template>
          </nav>

          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="flex items-center gap-2">
              <h2 class="text-base font-semibold text-foreground">
                {{ selectedNode.displayName }}
              </h2>
              <span class="text-xs text-muted-foreground"
                >{{ NODE_LABEL[selectedNode.type] }} · {{ selectedNode.code }}</span
              >
              <NvStatusBadge :value="selectedNode.active ? 'active' : 'disabled'" />
            </div>
            <div class="flex items-center gap-2">
              <NvButton
                v-for="childType in childTypesOfSelected"
                :key="childType"
                size="sm"
                type="button"
                @click="openCreateChild(selectedNode, childType)"
              >
                <PlusIcon aria-hidden="true" />
                新建{{ NODE_LABEL[childType] }}
              </NvButton>
              <MasterDataRowActions
                v-if="selectedActions"
                :row="selectedNode.item"
                :entity-label="NODE_LABEL[selectedNode.type]"
                :detail-fields="detailFields"
                @toggle="requestLifecycle"
                @edit="openEdit(selectedNode)"
              />
            </div>
          </div>

          <p v-if="detailLoading" class="text-sm text-muted-foreground">加载详情中…</p>
          <dl v-else class="grid gap-3 sm:grid-cols-2">
            <div v-for="field in detailFields" :key="field.label" class="grid gap-1">
              <dt class="text-xs text-muted-foreground">{{ field.label }}</dt>
              <dd class="text-sm text-foreground">{{ field.value || '无' }}</dd>
            </div>
          </dl>

          <!-- 子级计数 / 空子级出路 -->
          <div
            v-for="childType in childTypesOfSelected"
            :key="childType"
            class="border-t border-border/60 pt-3"
          >
            <p v-if="childCount(childType)" class="text-sm text-muted-foreground">
              该{{ NODE_LABEL[selectedNode.type] }}下有 {{ childCount(childType) }} 个{{
                NODE_LABEL[childType]
              }}。
            </p>
            <div v-else class="flex flex-wrap items-center gap-2">
              <p class="text-sm text-muted-foreground">
                该{{ NODE_LABEL[selectedNode.type] }}下还没有{{ NODE_LABEL[childType] }}。
              </p>
              <NvButton
                size="sm"
                variant="outline"
                type="button"
                @click="openCreateChild(selectedNode, childType)"
              >
                <PlusIcon aria-hidden="true" />
                新建{{ NODE_LABEL[childType] }}
              </NvButton>
            </div>
          </div>

          <!-- 工作中心 → 设备下钻出口 -->
          <div v-if="selectedNode.type === 'work-center'" class="border-t border-border/60 pt-3">
            <NvButton as-child size="sm" variant="outline">
              <RouterLink
                :to="{ path: '/master-data/devices', query: { workCenterCode: selectedNode.code } }"
              >
                查看该工作中心下设备 →
              </RouterLink>
            </NvButton>
          </div>
        </div>
      </section>
    </div>

    <!-- 编辑对话框（编码只读；改名 + 改挂上级） -->
    <NvDialog v-model:open="editOpen">
      <NvDialogContent class="sm:max-w-lg">
        <NvDialogHeader>
          <NvDialogTitle>{{ editTitle }}</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >{{ NODE_LABEL[editType] }} {{ editCode }}</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitEdit">
          <CarriedContextSummary
            :label="`${NODE_LABEL[editType]}标识`"
            :items="[{ label: `${NODE_LABEL[editType]}编码`, value: editCode }]"
          />
          <p v-if="editShowErrors && !canEdit" class="text-sm text-destructive" role="alert">
            请完整填写带 * 的必填项（已标红）。
          </p>
          <FormSectionTitle>基础信息</FormSectionTitle>
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.name)">
              <NvFieldLabel for="edit-name"
                >{{ NODE_LABEL[editType] }}名称
                <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput id="edit-name" v-model="editForm.name" autocomplete="off" required />
            </NvField>
            <NvField v-if="editType === 'site'">
              <NvFieldLabel for="edit-tz">时区</NvFieldLabel>
              <NvSearchSelect
                id="edit-tz"
                v-model="editForm.timezone"
                :options="editTimeZoneOptions"
                placeholder="选择时区"
                aria-label="时区"
              />
            </NvField>
          </NvFieldGroup>

          <!-- 归属（可改挂上级；选项取页内已加载列表，跨层不成环） -->
          <template v-if="editType !== 'site'">
            <FormSectionTitle>归属</FormSectionTitle>
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField
                v-if="editType === 'workshop'"
                :data-invalid="editShowErrors && !isNonEmpty(editForm.siteCode)"
              >
                <NvFieldLabel for="edit-site"
                  >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="editForm.siteCode">
                  <NvSelectTrigger id="edit-site"
                    ><NvSelectValue placeholder="请选择工厂"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="s in sites.items.value"
                      :key="s.code"
                      :value="s.code ?? ''"
                    >
                      {{ s.displayName ?? s.code }}
                    </NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <template v-if="editType === 'production-line'">
                <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.siteCode)">
                  <NvFieldLabel for="edit-line-site"
                    >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="editForm.siteCode">
                    <NvSelectTrigger id="edit-line-site"
                      ><NvSelectValue placeholder="请选择工厂"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="s in sites.items.value"
                        :key="s.code"
                        :value="s.code ?? NONE_OPTION"
                      >
                        {{ s.displayName ?? s.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel for="edit-line-workshop">所属车间</NvFieldLabel>
                  <NvSelect v-model="editWorkshopValue">
                    <NvSelectTrigger id="edit-line-workshop"
                      ><NvSelectValue placeholder="无（直挂工厂）"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem :value="NONE_OPTION">无（直挂工厂）</NvSelectItem>
                      <NvSelectItem
                        v-for="w in editWorkshopOptions"
                        :key="w.code"
                        :value="w.code ?? NONE_OPTION"
                      >
                        {{ w.displayName ?? w.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </template>
              <template v-if="editType === 'station'">
                <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.lineCode)">
                  <NvFieldLabel for="edit-station-line"
                    >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="editForm.lineCode">
                    <NvSelectTrigger id="edit-station-line"
                      ><NvSelectValue placeholder="请选择产线"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="l in lines.items.value"
                        :key="l.code"
                        :value="l.code ?? NONE_OPTION"
                      >
                        {{ l.displayName ?? l.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel for="edit-station-wc">关联工作中心</NvFieldLabel>
                  <DirectoryPicker
                    id="edit-station-wc"
                    v-model="editForm.workCenterCode"
                    directory-type="work-center"
                    :parent="{ lineCode: editForm.lineCode }"
                    placeholder="无"
                    clearable
                  />
                </NvField>
              </template>
              <template v-if="editType === 'work-center'">
                <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.plantCode)">
                  <NvFieldLabel for="edit-wc-plant"
                    >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="editForm.plantCode">
                    <NvSelectTrigger id="edit-wc-plant"
                      ><NvSelectValue placeholder="请选择工厂"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="s in sites.items.value"
                        :key="s.code"
                        :value="s.code ?? NONE_OPTION"
                      >
                        {{ s.displayName ?? s.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="editShowErrors && !isNonEmpty(editForm.lineCode)">
                  <NvFieldLabel for="edit-wc-line"
                    >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="editForm.lineCode">
                    <NvSelectTrigger id="edit-wc-line"
                      ><NvSelectValue placeholder="请选择产线"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="l in editLineOptions"
                        :key="l.code"
                        :value="l.code ?? NONE_OPTION"
                      >
                        {{ l.displayName ?? l.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </template>
            </NvFieldGroup>
          </template>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="editOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="editPending || editLoading">
              <Spinner v-if="editPending" aria-hidden="true" />
              保存修改
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
    <component
      :is="CREATE_DIALOGS[creating.type]"
      v-if="creating"
      :key="creating.session"
      v-model:open="createOpen"
      v-bind="creating.bindings"
    />
    <MasterDataLifecycleDialog :controller="lifecycle" />
  </BusinessLayout>
</template>
