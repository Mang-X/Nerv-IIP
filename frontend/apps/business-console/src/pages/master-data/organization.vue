<script setup lang="ts">
import type {
  BusinessConsoleCreateDepartmentRequest,
  BusinessConsoleCreateTeamRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { MasterDataTreeNodeData } from '@/components/masterData/MasterDataTreeNode.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import MasterDataTreeNode from '@/components/masterData/MasterDataTreeNode.vue'
import TeamMembersDialog from '@/components/masterData/TeamMembersDialog.vue'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  ScrollArea,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
} from '@nerv-iip/ui'
import {
  PlusIcon,
  RefreshCwIcon,
  SearchIcon,
  UsersIcon,
  UsersRoundIcon,
} from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '组织与班组' } })

// 层级树需尽量全量拼装：用较大 take 兜底（真正全量需后端全量端点 #373）。
const TREE_TAKE = 200

const departments = useMasterDataResource<BusinessConsoleCreateDepartmentRequest>('department')
const teams = useMasterDataResource<BusinessConsoleCreateTeamRequest>('team')
// 班组挂靠班次：新建班组要选班次，取班次列表填下拉（只读引用，不在本页维护班次）。
const shifts = useMasterDataResource('shift')
const deptActions = useMasterDataResourceActions('department')
const teamActions = useMasterDataResourceActions('team')

departments.filters.take = TREE_TAKE
teams.filters.take = TREE_TAKE
shifts.filters.take = TREE_TAKE

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function refreshAll() {
  void departments.refresh()
  void teams.refresh()
  void shifts.refresh()
}

// ================= 部门树（按 parentDepartmentCode 前端拼，缺则平铺） =================
// 注意：当前列表/详情端点未回传 parentDepartmentCode（仅创建时可设），故 Phase 1 多数环境
// 部门以平铺单层呈现；一旦后端在读侧回传该字段（#373），此处自动拼出多层树，无需改前端。
const DEPT_TYPE = 'department'
function toDeptNode(item: BusinessConsoleResourceItem): MasterDataTreeNodeData {
  return {
    type: DEPT_TYPE,
    code: item.code ?? '',
    displayName: item.displayName ?? item.code ?? '',
    active: item.active !== false,
    item,
    children: [],
  }
}
// 列表项可能（未来）带 parentDepartmentCode；用 unknown 读取避免假设其一定存在。
function parentCodeOf(item: BusinessConsoleResourceItem): string {
  const v = (item as Record<string, unknown>).parentDepartmentCode
  return typeof v === 'string' ? v : ''
}

const tree = computed<MasterDataTreeNodeData[]>(() => {
  const nodes = departments.items.value.map(toDeptNode)
  const byCode = new Map(nodes.map((n) => [n.code, n]))
  const roots: MasterDataTreeNodeData[] = []
  for (const n of nodes) {
    const parentCode = parentCodeOf(n.item)
    const parent = parentCode ? byCode.get(parentCode) : undefined
    if (parent && parent !== n) parent.children.push(n)
    else roots.push(n)
  }
  return roots
})

const totalDepartments = computed(() => departments.total.value)
const treePending = computed(() => departments.pending.value)
const treeTruncated = computed(() => departments.total.value > TREE_TAKE)
const treeListError = computed(() => {
  const e = departments.error.value
  return e instanceof Error ? e.message : e ? '部门数据加载失败，请刷新重试。' : ''
})

// ---- 树搜索 + 展开/折叠 ----
const treeSearch = ref('')
const expanded = reactive(new Set<string>())
function nodeKey(node: MasterDataTreeNodeData) {
  return `${node.type}:${node.code}`
}
function matchesSearch(node: MasterDataTreeNodeData, kw: string): boolean {
  if (!kw) return true
  const self = `${node.displayName} ${node.code}`.toLowerCase().includes(kw)
  return self || node.children.some((c) => matchesSearch(c, kw))
}
const filteredForest = computed<MasterDataTreeNodeData[]>(() => {
  const kw = treeSearch.value.trim().toLowerCase()
  if (!kw) return tree.value
  const prune = (nodes: MasterDataTreeNodeData[]): MasterDataTreeNodeData[] =>
    nodes
      .filter((n) => matchesSearch(n, kw))
      .map((n) => ({ ...n, children: prune(n.children) }))
  return prune(tree.value)
})

watch([tree, treeSearch], () => {
  const expandAll = (nodes: MasterDataTreeNodeData[]) => {
    for (const n of nodes) {
      if (n.children.length) {
        expanded.add(nodeKey(n))
        expandAll(n.children)
      }
    }
  }
  if (treeSearch.value.trim()) {
    expandAll(filteredForest.value)
    return
  }
  if (totalDepartments.value > 0 && totalDepartments.value < 50 && expanded.size === 0) {
    expandAll(tree.value)
  }
}, { immediate: true })

function toggleExpand(node: MasterDataTreeNodeData) {
  const key = nodeKey(node)
  if (expanded.has(key)) expanded.delete(key)
  else expanded.add(key)
}
const allExpanded = computed(() => {
  let hasParent = false
  const check = (nodes: MasterDataTreeNodeData[]): boolean =>
    nodes.every((n) => {
      if (!n.children.length) return true
      hasParent = true
      return expanded.has(nodeKey(n)) && check(n.children)
    })
  const ok = check(tree.value)
  return hasParent && ok
})
function expandAllToggle() {
  if (allExpanded.value) {
    expanded.clear()
    return
  }
  const add = (nodes: MasterDataTreeNodeData[]) => {
    for (const n of nodes) {
      if (n.children.length) {
        expanded.add(nodeKey(n))
        add(n.children)
      }
    }
  }
  add(tree.value)
}

// 部门可就地新建「子部门」。
function childLabelOf(type: string): string | undefined {
  return type === DEPT_TYPE ? '子部门' : undefined
}

// ---- 选中态（单选，持久 code）+ 详情 ----
const selectedKey = ref<string | null>(null)
const selectedNode = computed<MasterDataTreeNodeData | null>(() => {
  if (!selectedKey.value) return null
  let found: MasterDataTreeNodeData | null = null
  const walk = (nodes: MasterDataTreeNodeData[]) => {
    for (const n of nodes) {
      if (nodeKey(n) === selectedKey.value) {
        found = n
        return
      }
      walk(n.children)
    }
  }
  walk(tree.value)
  return found
})
function selectNode(node: MasterDataTreeNodeData) {
  selectedKey.value = nodeKey(node)
}
// 默认选中首个部门：右侧详情不空。
watch(tree, (roots) => {
  if (!selectedKey.value && roots.length > 0) selectedKey.value = nodeKey(roots[0]!)
}, { immediate: true })

const selectedPath = computed<MasterDataTreeNodeData[]>(() => {
  const target = selectedKey.value
  if (!target) return []
  const path: MasterDataTreeNodeData[] = []
  const walk = (nodes: MasterDataTreeNodeData[]): boolean => {
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

const detailLoading = shallowRef(false)
const detailExtra = shallowRef<Record<string, unknown> | null>(null)
watch(selectedNode, async (node) => {
  detailExtra.value = null
  if (!node?.code) return
  detailLoading.value = true
  try {
    detailExtra.value = (await deptActions.fetchDetail(node.code)) as Record<string, unknown> | null
  }
  finally {
    detailLoading.value = false
  }
})
const detailFields = computed(() => {
  const node = selectedNode.value
  if (!node) return [] as { label: string, value: string }[]
  const item = node.item
  return [
    { label: '部门编码', value: node.code },
    { label: '部门名称', value: node.displayName },
    { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
  ]
})

// ================= 班组列表（属选中部门下的班组维护出口） =================
// 列表端点不回传 team.departmentCode（仅创建时可设），故 Phase 1 无法按部门精确归集班组，
// 右侧展示全部班组并如实标注；在选中部门下「新建班组」时 departmentCode 已带入（写入侧可用）。
const teamRows = computed(() => teams.items.value)
const teamListError = computed(() => {
  const e = teams.error.value
  return e instanceof Error ? e.message : e ? '班组数据加载失败，请刷新重试。' : ''
})

// ================= 新建部门（含就地建子部门，父 code 预填只读） =================
const deptCreateOpen = ref(false)
const deptShowErrors = ref(false)
const deptForm = reactive({ code: '', name: '', parentDepartmentCode: '' })
// 父归属是否就地带入（决定上级部门字段是否只读）。
const deptParentLocked = ref(false)
const canCreateDept = computed(() => [deptForm.code, deptForm.name].every(isNonEmpty))
watch(deptCreateOpen, (open) => { if (open) deptShowErrors.value = false })
function resetDeptForm() {
  Object.assign(deptForm, { code: '', name: '', parentDepartmentCode: '' })
  deptParentLocked.value = false
}
function openCreateRootDept() {
  resetDeptForm()
  deptShowErrors.value = false
  deptCreateOpen.value = true
}
function openCreateChildDept(parent: MasterDataTreeNodeData) {
  resetDeptForm()
  deptForm.parentDepartmentCode = parent.code
  deptParentLocked.value = true
  deptShowErrors.value = false
  deptCreateOpen.value = true
}
async function submitCreateDept() {
  if (!canCreateDept.value) {
    deptShowErrors.value = true
    return
  }
  const name = deptForm.name.trim()
  try {
    await departments.create({
      organizationId: departments.filters.organizationId,
      environmentId: departments.filters.environmentId,
      code: deptForm.code.trim(),
      name,
      parentDepartmentCode: deptForm.parentDepartmentCode.trim() || null,
    })
    notifySuccess(`部门「${name}」已创建。`)
    resetDeptForm()
    deptShowErrors.value = false
    deptCreateOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ================= 编辑部门（编码 / 归属只读，仅改名；改挂上级见 #373） =================
const deptEditOpen = ref(false)
const deptEditShowErrors = ref(false)
const deptEditLoading = shallowRef(false)
const deptEditCode = shallowRef('')
const deptEditForm = reactive({ name: '' })
const canEditDept = computed(() => isNonEmpty(deptEditForm.name))
watch(deptEditOpen, (open) => { if (open) deptEditShowErrors.value = false })
async function openEditDept(node: MasterDataTreeNodeData) {
  if (!node.code) return
  deptEditCode.value = node.code
  deptEditShowErrors.value = false
  deptEditLoading.value = true
  deptEditOpen.value = true
  deptEditForm.name = node.displayName
  try {
    const d = (await deptActions.fetchDetail(node.code)) as Record<string, unknown> | undefined
    deptEditForm.name = (d?.name as string) || node.displayName
  }
  finally {
    deptEditLoading.value = false
  }
}
async function submitEditDept() {
  if (!canEditDept.value) {
    deptEditShowErrors.value = true
    return
  }
  const name = deptEditForm.name.trim()
  try {
    await deptActions.update(deptEditCode.value, { name })
    notifySuccess(`部门「${name}」已更新。`)
    deptEditShowErrors.value = false
    deptEditOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ================= 班组（新建：挂部门 + 班次；编辑：仅改名） =================
const teamOpen = ref(false)
const teamShowErrors = ref(false)
const teamEditingCode = shallowRef<string | null>(null)
const teamEditLoading = shallowRef(false)
// departmentLocked：从选中部门「在此部门下新建班组」时带入且只读。
const teamDepartmentLocked = ref(false)
const teamForm = reactive({ code: '', name: '', departmentCode: '', shiftCode: '' })
const canCreateTeam = computed(() => [teamForm.code, teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty))
const teamFormValid = computed(() => (teamEditingCode.value ? isNonEmpty(teamForm.name) : canCreateTeam.value))
watch(teamOpen, (open) => { if (open) teamShowErrors.value = false })
function resetTeamForm() {
  Object.assign(teamForm, { code: '', name: '', departmentCode: '', shiftCode: '' })
  teamDepartmentLocked.value = false
}
function openCreateTeam(departmentCode?: string) {
  teamEditingCode.value = null
  resetTeamForm()
  if (departmentCode) {
    teamForm.departmentCode = departmentCode
    teamDepartmentLocked.value = true
  }
  teamShowErrors.value = false
  teamOpen.value = true
}
async function openEditTeam(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  teamEditingCode.value = row.code
  teamShowErrors.value = false
  teamEditLoading.value = true
  teamOpen.value = true
  teamForm.code = row.code
  teamForm.name = row.displayName ?? ''
  try {
    const d = await teamActions.fetchDetail(row.code)
    teamForm.name = d?.name ?? row.displayName ?? ''
  }
  finally {
    teamEditLoading.value = false
  }
}
async function submitTeam() {
  if (teamEditingCode.value) {
    if (!isNonEmpty(teamForm.name)) {
      teamShowErrors.value = true
      return
    }
    try {
      await teamActions.update(teamEditingCode.value, { name: teamForm.name.trim() })
      notifySuccess(`班组「${teamForm.name.trim()}」已更新。`)
      resetTeamForm()
      teamEditingCode.value = null
      teamShowErrors.value = false
      teamOpen.value = false
    }
    catch (error) {
      notifyError(error)
    }
    return
  }
  if (!canCreateTeam.value) {
    teamShowErrors.value = true
    return
  }
  try {
    await teams.create({
      organizationId: teams.filters.organizationId,
      environmentId: teams.filters.environmentId,
      code: teamForm.code.trim(),
      name: teamForm.name.trim(),
      departmentCode: teamForm.departmentCode.trim(),
      shiftCode: teamForm.shiftCode.trim(),
    })
    notifySuccess(`班组「${teamForm.name.trim()}」已创建。`)
    resetTeamForm()
    teamShowErrors.value = false
    teamOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ---- 班组成员维护（弹窗，按行打开，复用 TeamMembersDialog）----
const membersOpen = ref(false)
const membersTeam = reactive({ code: '', name: '' })
function openMembers(row: BusinessConsoleResourceItem) {
  membersTeam.code = row.code ?? ''
  membersTeam.name = row.displayName ?? row.code ?? ''
  membersOpen.value = true
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="组织与班组" :breadcrumbs="[{ label: '基础数据' }]" :count="`${totalDepartments} 个部门`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="treePending" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreateRootDept">
          <PlusIcon aria-hidden="true" />
          新建部门
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      左侧部门树（选中父级可就地新建子部门），右侧维护该部门的班组与成员。工人来自系统用户，选择时按姓名 / 工号检索。
    </p>

    <SectionCards :columns="2">
      <SectionCard description="部门数" :value="departments.total.value" hint="组织 / 职能分组" />
      <SectionCard description="班组数" :value="teams.total.value" hint="挂靠部门与班次" />
    </SectionCards>

    <div class="grid gap-4 md:grid-cols-[320px_minmax(0,1fr)]">
      <!-- 左：部门树 -->
      <section class="flex flex-col gap-3 rounded-lg border border-border bg-card p-3" aria-label="部门树">
        <div class="relative">
          <SearchIcon class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input v-model="treeSearch" class="pl-8" placeholder="搜索部门编码、名称" aria-label="搜索部门树" />
        </div>
        <div class="flex items-center justify-between">
          <Button v-if="tree.length" size="sm" variant="ghost" type="button" class="h-7 px-2 text-xs" @click="expandAllToggle">
            {{ allExpanded ? '全部折叠' : '全部展开' }}
          </Button>
        </div>

        <p v-if="treeListError" class="text-sm text-destructive" role="alert">{{ treeListError }}</p>

        <ScrollArea class="h-[28rem]">
          <div v-if="treePending && !tree.length" class="px-1 py-2 text-sm text-muted-foreground">加载部门中…</div>
          <div v-else-if="!tree.length" class="grid gap-2 px-1 py-6 text-center">
            <UsersRoundIcon class="mx-auto size-8 text-muted-foreground" aria-hidden="true" />
            <p class="text-sm text-muted-foreground">还没有部门，点击创建第一条。</p>
            <Button size="sm" type="button" class="mx-auto" @click="openCreateRootDept">
              <PlusIcon aria-hidden="true" />
              新建部门
            </Button>
          </div>
          <div v-else-if="treeSearch.trim() && !filteredForest.length" class="grid gap-2 px-1 py-6 text-center">
            <p class="text-sm text-muted-foreground">没有匹配「{{ treeSearch.trim() }}」的部门。</p>
            <Button size="sm" variant="outline" type="button" class="mx-auto" @click="treeSearch = ''">清空搜索</Button>
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
              @create-child="openCreateChildDept"
            />
          </ul>
        </ScrollArea>

        <p v-if="treeTruncated" class="text-xs text-muted-foreground">
          部门较多，当前仅展示前 {{ TREE_TAKE }} 条；完整层级加载能力即将上线。
        </p>
      </section>

      <!-- 右：部门详情 + 该部门班组 -->
      <section class="rounded-lg border border-border bg-card p-4" aria-label="部门详情">
        <div v-if="!selectedNode" class="grid place-items-center gap-2 py-16 text-center">
          <UsersRoundIcon class="size-8 text-muted-foreground" aria-hidden="true" />
          <p class="text-sm text-muted-foreground">从左侧选择一个部门查看详情与班组。</p>
        </div>
        <div v-else class="grid gap-4">
          <nav class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground" aria-label="选中路径">
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
              <h2 class="text-base font-semibold text-foreground">{{ selectedNode.displayName }}</h2>
              <span class="text-xs text-muted-foreground">部门 · {{ selectedNode.code }}</span>
              <StatusBadge :value="selectedNode.active ? 'active' : 'disabled'" />
            </div>
            <div class="flex items-center gap-2">
              <Button size="sm" variant="outline" type="button" @click="openCreateChildDept(selectedNode)">
                <PlusIcon aria-hidden="true" />
                新建子部门
              </Button>
              <Button size="sm" type="button" @click="openCreateTeam(selectedNode.code)">
                <PlusIcon aria-hidden="true" />
                在此部门下新建班组
              </Button>
              <MasterDataRowActions
                :row="selectedNode.item"
                entity-label="部门"
                :detail-fields="detailFields"
                :actions="deptActions"
                @edit="openEditDept(selectedNode)"
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

          <!-- 该部门的班组 -->
          <div class="border-t border-border/60 pt-3">
            <div class="mb-2 flex items-center justify-between gap-2">
              <h3 class="text-sm font-medium text-foreground">班组</h3>
              <Button size="sm" type="button" @click="openCreateTeam(selectedNode.code)">
                <PlusIcon aria-hidden="true" />
                新建班组
              </Button>
            </div>
            <p class="mb-2 text-xs text-muted-foreground">
              班组与部门的精确归集即将上线，当前列出全部班组；可编辑、维护成员，新建时已带入本部门归属。
            </p>
            <p v-if="teamListError" class="text-sm text-destructive" role="alert">{{ teamListError }}</p>
            <div v-if="teams.pending.value && !teamRows.length" class="px-1 py-2 text-sm text-muted-foreground">加载班组中…</div>
            <div v-else-if="!teamRows.length" class="grid gap-2 px-1 py-6 text-center">
              <p class="text-sm text-muted-foreground">还没有班组，点击「新建班组」创建第一条。</p>
            </div>
            <ul v-else class="divide-y rounded-md border border-border">
              <li
                v-for="row in teamRows"
                :key="row.code ?? row.displayName"
                class="flex items-center justify-between gap-3 px-3 py-2"
              >
                <div class="flex min-w-0 items-center gap-2">
                  <span class="truncate text-sm" :class="row.active === false ? 'text-muted-foreground line-through' : ''">
                    {{ row.displayName ?? row.code ?? '无' }}
                  </span>
                  <span class="shrink-0 text-xs text-muted-foreground">{{ row.code }}</span>
                  <StatusBadge v-if="row.active === false" value="disabled" />
                </div>
                <div class="flex shrink-0 items-center gap-1">
                  <Button type="button" variant="ghost" size="sm" :disabled="!row.code" @click="openMembers(row)">
                    <UsersIcon aria-hidden="true" />管理成员
                  </Button>
                  <MasterDataRowActions
                    :row="row"
                    entity-label="班组"
                    :detail-fields="[{ label: '班组编码', value: row.code ?? '' }, { label: '班组名称', value: row.displayName ?? '' }]"
                    :actions="teamActions"
                    @edit="openEditTeam"
                  />
                </div>
              </li>
            </ul>
          </div>
        </div>
      </section>
    </div>

    <!-- 新建部门对话框（含就地建子部门，父归属只读） -->
    <Dialog v-model:open="deptCreateOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ deptParentLocked ? '新建子部门' : '新建部门' }}</DialogTitle>
          <DialogDescription>
            {{ deptParentLocked ? '在所选上级部门下登记一个子部门。上级已带入且不可更改。带 * 为必填项。' : '登记一个组织部门，可选挂靠上级部门。带 * 为必填项。' }}
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreateDept">
          <p v-if="deptShowErrors && !canCreateDept" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field :data-invalid="deptShowErrors && !isNonEmpty(deptForm.code)">
              <FieldLabel for="dept-code">部门编码 <span class="text-destructive">*</span></FieldLabel>
              <Input id="dept-code" v-model="deptForm.code" autocomplete="off" required />
            </Field>
            <Field :data-invalid="deptShowErrors && !isNonEmpty(deptForm.name)">
              <FieldLabel for="dept-name">部门名称 <span class="text-destructive">*</span></FieldLabel>
              <Input id="dept-name" v-model="deptForm.name" autocomplete="off" required />
            </Field>
            <Field v-if="deptParentLocked">
              <FieldLabel for="dept-parent">上级部门</FieldLabel>
              <Input id="dept-parent" :model-value="deptForm.parentDepartmentCode" disabled />
            </Field>
            <Field v-else>
              <FieldLabel for="dept-parent">上级部门</FieldLabel>
              <Select v-model="deptForm.parentDepartmentCode">
                <SelectTrigger id="dept-parent"><SelectValue placeholder="无（顶级部门）" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="d in departments.items.value" :key="d.code" :value="d.code ?? ''">
                    {{ d.displayName ?? d.code }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FieldDescription>留空表示顶级部门。</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" @click="deptCreateOpen = false">取消</Button>
            <Button type="submit" :disabled="departments.createPending.value">
              <Spinner v-if="departments.createPending.value" aria-hidden="true" />保存部门
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <!-- 编辑部门对话框（编码 / 归属只读，仅改名；改挂上级见 #373） -->
    <Dialog v-model:open="deptEditOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>编辑部门 · {{ deptEditCode }}</DialogTitle>
          <DialogDescription>修改部门名称（编码与归属不可修改）。带 * 为必填项。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitEditDept">
          <p v-if="deptEditShowErrors && !canEditDept" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="dept-edit-code">部门编码</FieldLabel>
              <Input id="dept-edit-code" :model-value="deptEditCode" disabled />
            </Field>
            <Field :data-invalid="deptEditShowErrors && !isNonEmpty(deptEditForm.name)">
              <FieldLabel for="dept-edit-name">部门名称 <span class="text-destructive">*</span></FieldLabel>
              <Input id="dept-edit-name" v-model="deptEditForm.name" autocomplete="off" required />
            </Field>
          </FieldGroup>
          <p class="text-xs text-muted-foreground">归属（上级部门）创建后不可更改，改挂上级功能即将上线。</p>
          <DialogFooter>
            <Button type="button" variant="outline" @click="deptEditOpen = false">取消</Button>
            <Button type="submit" :disabled="deptActions.updatePending.value || deptEditLoading">
              <Spinner v-if="deptActions.updatePending.value" aria-hidden="true" />保存修改
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <!-- 班组对话框（新建：挂部门 + 班次；编辑：仅改名，编码 / 归属只读） -->
    <Dialog v-model:open="teamOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ teamEditingCode ? `编辑班组 · ${teamEditingCode}` : '新建班组' }}</DialogTitle>
          <DialogDescription>{{ teamEditingCode ? '修改班组名称（编码与归属不可修改）。带 * 为必填项。' : '将班组挂靠到部门与班次。带 * 为必填项。' }}</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitTeam">
          <p v-if="teamShowErrors && !teamFormValid" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field :data-invalid="teamShowErrors && !isNonEmpty(teamForm.code)">
              <FieldLabel for="team-code">班组编码 <span class="text-destructive">*</span></FieldLabel>
              <Input id="team-code" v-model="teamForm.code" autocomplete="off" :disabled="!!teamEditingCode" required />
            </Field>
            <Field :data-invalid="teamShowErrors && !isNonEmpty(teamForm.name)">
              <FieldLabel for="team-name">班组名称 <span class="text-destructive">*</span></FieldLabel>
              <Input id="team-name" v-model="teamForm.name" autocomplete="off" required />
            </Field>
            <Field v-if="teamDepartmentLocked && !teamEditingCode">
              <FieldLabel for="team-dept-locked">所属部门</FieldLabel>
              <Input id="team-dept-locked" :model-value="teamForm.departmentCode" disabled />
            </Field>
            <Field v-else :data-invalid="teamShowErrors && !teamEditingCode && !isNonEmpty(teamForm.departmentCode)">
              <FieldLabel for="team-dept">所属部门 <span class="text-destructive">*</span></FieldLabel>
              <Select v-model="teamForm.departmentCode" :disabled="!!teamEditingCode">
                <SelectTrigger id="team-dept"><SelectValue placeholder="请选择部门" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="d in departments.items.value" :key="d.code" :value="d.code ?? ''">
                    {{ d.displayName ?? d.code }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field :data-invalid="teamShowErrors && !teamEditingCode && !isNonEmpty(teamForm.shiftCode)">
              <FieldLabel for="team-shift">所属班次 <span class="text-destructive">*</span></FieldLabel>
              <Select v-model="teamForm.shiftCode" :disabled="!!teamEditingCode">
                <SelectTrigger id="team-shift"><SelectValue placeholder="请选择班次" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="s in shifts.items.value" :key="s.code" :value="s.code ?? ''">
                    {{ s.displayName ?? s.code }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FieldDescription>班次在「排班与日历」页维护。</FieldDescription>
            </Field>
          </FieldGroup>
          <p v-if="teamEditingCode" class="text-xs text-muted-foreground">归属（部门 / 班次）创建后不可更改，改挂功能即将上线。</p>
          <DialogFooter>
            <Button type="button" variant="outline" @click="teamOpen = false">取消</Button>
            <Button type="submit" :disabled="teams.createPending.value || teamActions.updatePending.value || teamEditLoading">
              <Spinner v-if="teams.createPending.value || teamActions.updatePending.value" aria-hidden="true" />{{ teamEditingCode ? '保存修改' : '保存班组' }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <TeamMembersDialog v-model:open="membersOpen" :team-code="membersTeam.code" :team-name="membersTeam.name" />
  </BusinessLayout>
</template>
