import type {
  BusinessConsoleCreateDepartmentRequest,
  BusinessConsoleCreateTeamRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { MasterDataTreeNodeData } from '@/components/masterData/MasterDataTreeNode.vue'
import { useIncludeDisabledFilter } from '@/composables/master-data/masterDataIncludeDisabled'
import { useMasterDataLifecycleConfirm } from '@/composables/master-data/masterDataLifecycleConfirm'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/master-data'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

export function useOrganizationPage() {
  const TREE_TAKE = 5000

  const departments = useMasterDataResource<BusinessConsoleCreateDepartmentRequest>('department', {
    all: true,
  })
  const teams = useMasterDataResource<BusinessConsoleCreateTeamRequest>('team', { all: true })
  // 班组挂靠班次：「所属班次」用班次选择器（可就地新增）；这里的列表只用来在只有一个班次时自动选中。
  const shifts = useMasterDataResource('shift', { all: true })
  const deptActions = useMasterDataResourceActions('department')
  const teamActions = useMasterDataResourceActions('team')
  // 停用/启用确认框收在页面层单实例，行操作只负责指向当前行（#1591）。
  const lifecycle = useMasterDataLifecycleConfirm()

  // 停用后的部门/班组/班次默认不在列表里，「启用」入口就永远够不到；三者共用一个开关（#1594）。
  const includeDisabled = useIncludeDisabledFilter([
    departments.filters,
    teams.filters,
    shifts.filters,
  ])

  function isNonEmpty(value: string) {
    return value.trim().length > 0
  }
  function refreshAll() {
    void departments.refresh()
    void teams.refresh()
    void shifts.refresh()
  }

  // ================= 部门多层树（按 parentDepartmentCode 前端拼） =================
  // 列表回传 parentDepartmentCode（#375）：据此拼多层部门树；缺/找不到上级的视为顶级根。
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
  function parentCodeOf(item: BusinessConsoleResourceItem): string {
    return item.parentDepartmentCode ?? ''
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

  // 防环：某部门的全部子孙 code（含自身）——改挂上级时不能挂到自己或子孙下。
  function descendantCodesOf(code: string): Set<string> {
    const childrenByParent = new Map<string, string[]>()
    for (const item of departments.items.value) {
      const parent = parentCodeOf(item)
      const self = item.code ?? ''
      if (!self) continue
      const list = childrenByParent.get(parent) ?? []
      list.push(self)
      childrenByParent.set(parent, list)
    }
    const result = new Set<string>([code])
    const stack = [code]
    while (stack.length) {
      const current = stack.pop()!
      for (const child of childrenByParent.get(current) ?? []) {
        if (!result.has(child)) {
          result.add(child)
          stack.push(child)
        }
      }
    }
    return result
  }

  const totalDepartments = computed(() => departments.total.value)
  const treePending = computed(() => departments.pending.value)
  const treeTruncated = computed(() => departments.total.value > TREE_TAKE)
  const treeListError = computed(() => {
    const e = departments.error.value
    return inlineErrorMessage(e, '部门数据加载失败，请刷新重试。')
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
      nodes.filter((n) => matchesSearch(n, kw)).map((n) => ({ ...n, children: prune(n.children) }))
    return prune(tree.value)
  })

  watch(
    [tree, treeSearch],
    () => {
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
    },
    { immediate: true },
  )

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
  watch(
    tree,
    (roots) => {
      if (!selectedKey.value && roots.length > 0) selectedKey.value = nodeKey(roots[0]!)
    },
    { immediate: true },
  )

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
      detailExtra.value = (await deptActions.fetchDetail(node.code)) as Record<
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
    const item = node.item
    return [
      { label: '部门编码', value: node.code },
      { label: '部门名称', value: node.displayName },
      { label: '更新时间', value: formatDateTime(item.snapshotVersion) },
    ]
  })

  // ================= 班组按部门归集（属选中部门下的班组维护出口） =================
  // 列表回传 team.departmentCode（#375）：选中某部门时，右侧只列归属该部门的班组。
  const teamRows = computed(() => {
    const deptCode = selectedNode.value?.code
    if (!deptCode) return []
    return teams.items.value.filter((t) => (t.departmentCode ?? '') === deptCode)
  })
  const teamListError = computed(() => {
    const e = teams.error.value
    return inlineErrorMessage(e, '班组数据加载失败，请刷新重试。')
  })

  // ================= 新建部门（含就地建子部门，父 code 预填只读） =================
  const deptCreateOpen = ref(false)
  const deptShowErrors = ref(false)
  const deptForm = reactive({ name: '', parentDepartmentCode: '' })
  // 父归属是否就地带入（带入时不再让用户选，改为只读上下文展示）。
  const deptParentLocked = ref(false)
  const deptParentName = ref('')
  const canCreateDept = computed(() => isNonEmpty(deptForm.name))
  watch(deptCreateOpen, (open) => {
    if (open) deptShowErrors.value = false
  })
  function resetDeptForm() {
    Object.assign(deptForm, { name: '', parentDepartmentCode: '' })
    deptParentLocked.value = false
    deptParentName.value = ''
  }
  function openCreateRootDept() {
    resetDeptForm()
    deptShowErrors.value = false
    deptCreateOpen.value = true
  }
  function openCreateChildDept(parent: MasterDataTreeNodeData) {
    resetDeptForm()
    deptForm.parentDepartmentCode = parent.code
    deptParentName.value = parent.displayName || parent.code
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
        name,
        parentDepartmentCode: deptForm.parentDepartmentCode.trim() || null,
      })
      notifySuccess(`部门「${name}」已创建。`)
      resetDeptForm()
      deptShowErrors.value = false
      deptCreateOpen.value = false
    } catch (error) {
      notifyOperationFailure('创建部门失败', error, '创建部门失败，请稍后重试。')
    }
  }

  // ================= 编辑部门（编码只读；改名 + 改挂上级 parentDepartmentCode） =================
  const deptEditOpen = ref(false)
  const deptEditShowErrors = ref(false)
  const deptEditLoading = shallowRef(false)
  const deptEditCode = shallowRef('')
  const deptEditForm = reactive({ name: '', parentDepartmentCode: '' })
  const canEditDept = computed(() => isNonEmpty(deptEditForm.name))
  // 防环：编辑部门时，上级候选排除自身及其全部子孙（不能把部门挂到自己的子孙下）。
  const deptParentOptions = computed(() => {
    if (!deptEditCode.value) return departments.items.value
    const blocked = descendantCodesOf(deptEditCode.value)
    return departments.items.value.filter((d) => !blocked.has(d.code ?? ''))
  })

  // reka 的 SelectItem 不允许空串 value（空串保留给“清空占位”）。顶级部门（无上级）
  // 用哨兵值表示，仅作用于下拉绑定；提交仍按 空串→null 处理（见 saveEditDept）。
  const NONE_PARENT = '__root__'
  const deptEditParent = computed({
    get: () => deptEditForm.parentDepartmentCode || NONE_PARENT,
    set: (v) => {
      deptEditForm.parentDepartmentCode = v === NONE_PARENT ? '' : v
    },
  })
  watch(deptEditOpen, (open) => {
    if (open) deptEditShowErrors.value = false
  })
  async function openEditDept(node: MasterDataTreeNodeData) {
    if (!node.code) return
    deptEditCode.value = node.code
    deptEditShowErrors.value = false
    deptEditLoading.value = true
    deptEditOpen.value = true
    deptEditForm.name = node.displayName
    deptEditForm.parentDepartmentCode = parentCodeOf(node.item)
    try {
      const d = (await deptActions.fetchDetail(node.code)) as Record<string, unknown> | undefined
      deptEditForm.name = (d?.name as string) || node.displayName
      deptEditForm.parentDepartmentCode =
        (d?.parentDepartmentCode as string) ?? parentCodeOf(node.item)
    } finally {
      deptEditLoading.value = false
    }
  }
  async function submitEditDept() {
    if (!canEditDept.value) {
      deptEditShowErrors.value = true
      return
    }
    const newParent = deptEditForm.parentDepartmentCode.trim()
    // 防环兜底：拦截把部门挂到自己 / 子孙下的非法归属。
    if (newParent && descendantCodesOf(deptEditCode.value).has(newParent)) {
      notifyError('不能把部门挂到自己或其下级部门下。')
      return
    }
    const name = deptEditForm.name.trim()
    try {
      await deptActions.update(deptEditCode.value, {
        name,
        parentDepartmentCode: newParent || null,
      })
      notifySuccess(`部门「${name}」已更新。`)
      deptEditShowErrors.value = false
      deptEditOpen.value = false
    } catch (error) {
      notifyOperationFailure('更新部门失败', error, '更新部门失败，请稍后重试。')
    }
  }

  // ================= 班组（新建：挂部门 + 班次；编辑：仅改名） =================
  const departmentNameByCode = computed(
    () =>
      new Map(departments.items.value.map((d) => [d.code ?? '', d.displayName ?? d.code ?? ''])),
  )
  function departmentLabel(code: string) {
    return departmentNameByCode.value.get(code) || code
  }

  const teamOpen = ref(false)
  const teamShowErrors = ref(false)
  const teamEditingCode = shallowRef<string | null>(null)
  const teamEditLoading = shallowRef(false)
  // departmentLocked：从选中部门「在此部门下新建班组」时带入且只读。
  const teamDepartmentLocked = ref(false)
  const teamForm = reactive({
    code: '',
    name: '',
    departmentCode: '',
    shiftCode: '',
    workshopCode: '',
  })
  const canCreateTeam = computed(() =>
    [teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty),
  )
  // 编辑也校验归属（部门 / 班次可改但不可空）；新建额外校验 code。
  const teamFormValid = computed(() =>
    teamEditingCode.value
      ? [teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty)
      : canCreateTeam.value,
  )
  watch(teamOpen, (open) => {
    if (open) teamShowErrors.value = false
  })
  function resetTeamForm() {
    Object.assign(teamForm, {
      code: '',
      name: '',
      departmentCode: '',
      shiftCode: '',
      workshopCode: '',
    })
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
  // 缺省值：班次只有一个可选时自动选中，不让用户为唯一选项再点一次。
  watch(
    [teamOpen, () => shifts.items.value],
    () => {
      if (!teamOpen.value || teamEditingCode.value) return
      if (teamForm.shiftCode) return
      const only = shifts.items.value.length === 1 ? shifts.items.value[0] : undefined
      if (only?.code) teamForm.shiftCode = only.code
    },
    { immediate: true },
  )
  async function openEditTeam(row: BusinessConsoleResourceItem) {
    if (!row.code) return
    teamEditingCode.value = row.code
    teamShowErrors.value = false
    teamEditLoading.value = true
    teamOpen.value = true
    teamForm.code = row.code
    teamForm.name = row.displayName ?? ''
    teamForm.departmentCode = row.departmentCode ?? ''
    teamForm.shiftCode = row.shiftCode ?? ''
    teamForm.workshopCode = row.workshopCode ?? ''
    try {
      const d = await teamActions.fetchDetail(row.code)
      teamForm.name = d?.name ?? row.displayName ?? ''
      teamForm.departmentCode =
        (d?.departmentCode as string | undefined) ?? row.departmentCode ?? ''
      teamForm.shiftCode = (d?.shiftCode as string | undefined) ?? row.shiftCode ?? ''
      teamForm.workshopCode = (d?.workshopCode as string | undefined) ?? row.workshopCode ?? ''
    } finally {
      teamEditLoading.value = false
    }
  }
  async function submitTeam() {
    if (teamEditingCode.value) {
      // 编辑：改名 + 改挂部门 / 班次（归属可改，编码只读）。
      if (![teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty)) {
        teamShowErrors.value = true
        return
      }
      try {
        await teamActions.update(teamEditingCode.value, {
          name: teamForm.name.trim(),
          departmentCode: teamForm.departmentCode.trim(),
          shiftCode: teamForm.shiftCode.trim(),
          workshopCode: teamForm.workshopCode.trim() || null,
        })
        notifySuccess(`班组「${teamForm.name.trim()}」已更新。`)
        resetTeamForm()
        teamEditingCode.value = null
        teamShowErrors.value = false
        teamOpen.value = false
      } catch (error) {
        notifyOperationFailure('更新班组失败', error, '更新班组失败，请稍后重试。')
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
        name: teamForm.name.trim(),
        departmentCode: teamForm.departmentCode.trim(),
        shiftCode: teamForm.shiftCode.trim(),
        workshopCode: teamForm.workshopCode.trim() || null,
      })
      notifySuccess(`班组「${teamForm.name.trim()}」已创建。`)
      resetTeamForm()
      teamShowErrors.value = false
      teamOpen.value = false
    } catch (error) {
      notifyOperationFailure('保存班组失败', error, '保存班组失败，请稍后重试。')
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

  return {
    NONE_PARENT,
    TREE_TAKE,
    allExpanded,
    canCreateDept,
    canEditDept,
    childLabelOf,
    departmentLabel,
    departments,
    deptActions,
    deptCreateOpen,
    deptEditCode,
    deptEditForm,
    deptEditLoading,
    deptEditOpen,
    deptEditParent,
    deptEditShowErrors,
    deptForm,
    deptParentLocked,
    deptParentName,
    deptParentOptions,
    deptShowErrors,
    detailFields,
    detailLoading,
    expandAllToggle,
    expanded,
    filteredForest,
    includeDisabled,
    isNonEmpty,
    lifecycle,
    membersOpen,
    membersTeam,
    nodeKey,
    openCreateChildDept,
    openCreateRootDept,
    openCreateTeam,
    openEditDept,
    openEditTeam,
    openMembers,
    refreshAll,
    selectNode,
    selectedKey,
    selectedNode,
    selectedPath,
    submitCreateDept,
    submitEditDept,
    submitTeam,
    teamActions,
    teamDepartmentLocked,
    teamEditLoading,
    teamEditingCode,
    teamForm,
    teamFormValid,
    teamListError,
    teamOpen,
    teamRows,
    teamShowErrors,
    teams,
    toggleExpand,
    totalDepartments,
    tree,
    treeListError,
    treePending,
    treeSearch,
    treeTruncated,
  }
}
