<script setup lang="ts">
import type {
  BusinessConsoleCreateProductionLineRequest,
  BusinessConsoleCreateSiteRequest,
  BusinessConsoleCreateWorkCenterRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useBusinessWorkshops, useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  toast,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工厂与产线' } })

const DEFAULT_TIMEZONE = 'Asia/Shanghai'
const WORK_CENTER_DEFAULTS = {
  resourceType: 'work-center',
  capacityUnit: 'minutes',
  capacityMinutesPerDay: 480,
  finiteCapacity: true,
}

const sites = useMasterDataResource<BusinessConsoleCreateSiteRequest>('site')
const workshops = useBusinessWorkshops()
const lines = useMasterDataResource<BusinessConsoleCreateProductionLineRequest>('production-line')
const workCenters = useMasterDataResource<BusinessConsoleCreateWorkCenterRequest>('work-center')
const siteActions = useMasterDataResourceActions('site')
const workshopActions = useMasterDataResourceActions('workshop')
const lineActions = useMasterDataResourceActions('production-line')
const wcActions = useMasterDataResourceActions('work-center')

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

const workshopColumns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'siteCode', header: '所属工厂', width: 'w-32' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

const facilityActionError = computed(() =>
  formatError(
    siteActions.actionError.value
    ?? workshopActions.actionError.value
    ?? lineActions.actionError.value
    ?? wcActions.actionError.value,
  ),
)
function siteDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '工厂编码', value: row.code ?? '' },
    { label: '工厂名称', value: row.displayName ?? '' },
  ]
}
function workshopDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '车间编码', value: row.code ?? '' },
    { label: '车间名称', value: row.displayName ?? '' },
    { label: '所属工厂', value: row.siteCode ?? '' },
  ]
}
function lineDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '产线编码', value: row.code ?? '' },
    { label: '产线名称', value: row.displayName ?? '' },
    { label: '所属工厂', value: row.siteCode ?? '' },
  ]
}
function workCenterDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '工作中心编码', value: row.code ?? '' },
    { label: '工作中心名称', value: row.displayName ?? '' },
    { label: '所属工厂', value: row.plantCode ?? '' },
    { label: '所属产线', value: row.lineCode ?? '' },
    { label: '日产能（分钟）', value: row.capacityMinutesPerDay != null ? String(row.capacityMinutesPerDay) : '' },
  ]
}

function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? ''}:${item.code || item.displayName || ''}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}

// ---- 工厂 ----
const siteKeyword = ref('')
const sitePage = ref(1)
const sitePageSize = ref('10')
const siteOpen = ref(false)
const siteShowErrors = ref(false)
// 编辑态：null=新建，否则=正在编辑的编码（编码不可改）。
const siteEditingCode = shallowRef<string | null>(null)
const siteEditLoading = shallowRef(false)
const siteForm = reactive({ code: '', name: '', timezone: DEFAULT_TIMEZONE })
const siteRows = computed(() => filterRows(sites.items.value, siteKeyword.value))
const canCreateSite = computed(() => [siteForm.code, siteForm.name, siteForm.timezone].every(isNonEmpty))
const siteCreateError = computed(() => formatError(sites.createError.value))
const siteListError = computed(() => formatError(sites.error.value))

watch(siteOpen, (open) => { if (open) siteShowErrors.value = false })
watch([siteKeyword, sitePageSize], () => { sitePage.value = 1 })
watch([sitePage, sitePageSize], () => {
  sites.filters.skip = (sitePage.value - 1) * (Number(sitePageSize.value) || 10)
  sites.filters.take = Number(sitePageSize.value) || 10
}, { immediate: true })

function resetSiteForm() {
  Object.assign(siteForm, { code: '', name: '', timezone: DEFAULT_TIMEZONE })
}
function openCreateSite() {
  siteEditingCode.value = null
  resetSiteForm()
  siteShowErrors.value = false
  siteOpen.value = true
}
async function openEditSite(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  siteEditingCode.value = row.code
  siteShowErrors.value = false
  siteEditLoading.value = true
  siteOpen.value = true
  try {
    const d = await siteActions.fetchDetail(row.code)
    Object.assign(siteForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      timezone: d?.timezone || DEFAULT_TIMEZONE,
    })
  }
  finally {
    siteEditLoading.value = false
  }
}
async function submitSite() {
  if (!canCreateSite.value) {
    siteShowErrors.value = true
    return
  }
  if (siteEditingCode.value) {
    await siteActions.update(siteEditingCode.value, {
      name: siteForm.name.trim(),
      timezone: siteForm.timezone.trim(),
    })
    toast.success(`工厂「${siteForm.name.trim()}」已更新。`)
  }
  else {
    await sites.create({
      organizationId: sites.filters.organizationId,
      environmentId: sites.filters.environmentId,
      code: siteForm.code.trim(),
      name: siteForm.name.trim(),
      timezone: siteForm.timezone.trim(),
    })
    toast.success(`工厂「${siteForm.name.trim()}」已创建。`)
  }
  resetSiteForm()
  siteEditingCode.value = null
  siteShowErrors.value = false
  siteOpen.value = false
}

// ---- 车间 ----
const workshopKeyword = ref('')
const workshopPage = ref(1)
const workshopPageSize = ref('10')
const workshopOpen = ref(false)
const workshopShowErrors = ref(false)
const workshopEditingCode = shallowRef<string | null>(null)
const workshopEditLoading = shallowRef(false)
const workshopForm = reactive({ code: '', name: '', siteCode: '', managerUserId: '', description: '' })
const workshopRows = computed(() => filterRows(workshops.workshops.value, workshopKeyword.value))
const canCreateWorkshop = computed(() => [workshopForm.code, workshopForm.name, workshopForm.siteCode].every(isNonEmpty))
const workshopCreateError = computed(() => formatError(workshops.createWorkshopError.value))
const workshopListError = computed(() => formatError(workshops.workshopsError.value))

watch(workshopOpen, (open) => { if (open) workshopShowErrors.value = false })
watch([workshopKeyword, workshopPageSize], () => { workshopPage.value = 1 })
watch([workshopPage, workshopPageSize], () => {
  workshops.filters.skip = (workshopPage.value - 1) * (Number(workshopPageSize.value) || 10)
  workshops.filters.take = Number(workshopPageSize.value) || 10
}, { immediate: true })

function resetWorkshopForm() {
  Object.assign(workshopForm, { code: '', name: '', siteCode: '', managerUserId: '', description: '' })
}
function openCreateWorkshop() {
  workshopEditingCode.value = null
  resetWorkshopForm()
  workshopShowErrors.value = false
  workshopOpen.value = true
}
async function openEditWorkshop(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  workshopEditingCode.value = row.code
  workshopShowErrors.value = false
  workshopEditLoading.value = true
  workshopOpen.value = true
  try {
    const d = await workshopActions.fetchDetail(row.code)
    // 详情未返回 managerUserId/description（列表/详情形状均无），编辑时留空由用户按需重填。
    Object.assign(workshopForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      siteCode: d?.siteCode ?? row.siteCode ?? '',
      managerUserId: '',
      description: '',
    })
  }
  finally {
    workshopEditLoading.value = false
  }
}
async function submitWorkshop() {
  if (!canCreateWorkshop.value) {
    workshopShowErrors.value = true
    return
  }
  const manager = workshopForm.managerUserId.trim()
  const note = workshopForm.description.trim()
  if (workshopEditingCode.value) {
    await workshopActions.update(workshopEditingCode.value, {
      name: workshopForm.name.trim(),
      siteCode: workshopForm.siteCode.trim(),
      managerUserId: manager || null,
      description: note || null,
    })
    toast.success(`车间「${workshopForm.name.trim()}」已更新。`)
  }
  else {
    await workshops.createWorkshop({
      organizationId: workshops.filters.organizationId,
      environmentId: workshops.filters.environmentId,
      code: workshopForm.code.trim(),
      name: workshopForm.name.trim(),
      siteCode: workshopForm.siteCode.trim(),
      ...(manager ? { managerUserId: manager } : {}),
      ...(note ? { description: note } : {}),
    })
    toast.success(`车间「${workshopForm.name.trim()}」已创建。`)
  }
  resetWorkshopForm()
  workshopEditingCode.value = null
  workshopShowErrors.value = false
  workshopOpen.value = false
}

// ---- 产线 ----
const lineKeyword = ref('')
const linePage = ref(1)
const linePageSize = ref('10')
const lineOpen = ref(false)
const lineShowErrors = ref(false)
const lineEditingCode = shallowRef<string | null>(null)
const lineEditLoading = shallowRef(false)
const lineForm = reactive({ code: '', name: '', siteCode: '', workshopCode: '' })
const lineRows = computed(() => filterRows(lines.items.value, lineKeyword.value))
const canCreateLine = computed(() => [lineForm.code, lineForm.name, lineForm.siteCode].every(isNonEmpty))
const lineCreateError = computed(() => formatError(lines.createError.value))
const lineListError = computed(() => formatError(lines.error.value))

watch(lineOpen, (open) => { if (open) lineShowErrors.value = false })
watch([lineKeyword, linePageSize], () => { linePage.value = 1 })
watch([linePage, linePageSize], () => {
  lines.filters.skip = (linePage.value - 1) * (Number(linePageSize.value) || 10)
  lines.filters.take = Number(linePageSize.value) || 10
}, { immediate: true })

function resetLineForm() {
  Object.assign(lineForm, { code: '', name: '', siteCode: '', workshopCode: '' })
}
function openCreateLine() {
  lineEditingCode.value = null
  resetLineForm()
  lineShowErrors.value = false
  lineOpen.value = true
}
async function openEditLine(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  lineEditingCode.value = row.code
  lineShowErrors.value = false
  lineEditLoading.value = true
  lineOpen.value = true
  try {
    const d = await lineActions.fetchDetail(row.code)
    Object.assign(lineForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      siteCode: d?.siteCode ?? row.siteCode ?? '',
      workshopCode: row.workshopCode ?? '',
    })
  }
  finally {
    lineEditLoading.value = false
  }
}
async function submitLine() {
  if (!canCreateLine.value) {
    lineShowErrors.value = true
    return
  }
  const workshopCode = lineForm.workshopCode.trim()
  if (lineEditingCode.value) {
    await lineActions.update(lineEditingCode.value, {
      name: lineForm.name.trim(),
      siteCode: lineForm.siteCode.trim(),
      workshopCode: workshopCode || null,
    })
    toast.success(`产线「${lineForm.name.trim()}」已更新。`)
  }
  else {
    await lines.create({
      organizationId: lines.filters.organizationId,
      environmentId: lines.filters.environmentId,
      code: lineForm.code.trim(),
      name: lineForm.name.trim(),
      siteCode: lineForm.siteCode.trim(),
      ...(workshopCode ? { workshopCode } : {}),
    })
    toast.success(`产线「${lineForm.name.trim()}」已创建。`)
  }
  resetLineForm()
  lineEditingCode.value = null
  lineShowErrors.value = false
  lineOpen.value = false
}

// ---- 工作中心 ----
const wcKeyword = ref('')
const wcPage = ref(1)
const wcPageSize = ref('10')
const wcOpen = ref(false)
const wcShowErrors = ref(false)
const wcEditingCode = shallowRef<string | null>(null)
const wcEditLoading = shallowRef(false)
const wcForm = reactive({ code: '', name: '', plantCode: '', lineCode: '', workshopCode: '', defaultCalendarCode: '', capacityMinutesPerDay: '480' })
const wcRows = computed(() => filterRows(workCenters.items.value, wcKeyword.value))
const canCreateWorkCenter = computed(() =>
  [wcForm.code, wcForm.name, wcForm.plantCode, wcForm.lineCode, wcForm.defaultCalendarCode].every(isNonEmpty)
  && (Number(wcForm.capacityMinutesPerDay) || 0) > 0,
)
const wcCreateError = computed(() => formatError(workCenters.createError.value))
const wcListError = computed(() => formatError(workCenters.error.value))

watch(wcOpen, (open) => { if (open) wcShowErrors.value = false })
watch([wcKeyword, wcPageSize], () => { wcPage.value = 1 })
watch([wcPage, wcPageSize], () => {
  workCenters.filters.skip = (wcPage.value - 1) * (Number(wcPageSize.value) || 10)
  workCenters.filters.take = Number(wcPageSize.value) || 10
}, { immediate: true })

function resetWcForm() {
  Object.assign(wcForm, { code: '', name: '', plantCode: '', lineCode: '', workshopCode: '', defaultCalendarCode: '', capacityMinutesPerDay: '480' })
}
function openCreateWorkCenter() {
  wcEditingCode.value = null
  resetWcForm()
  wcShowErrors.value = false
  wcOpen.value = true
}
async function openEditWorkCenter(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  wcEditingCode.value = row.code
  wcShowErrors.value = false
  wcEditLoading.value = true
  wcOpen.value = true
  try {
    const d = await wcActions.fetchDetail(row.code)
    Object.assign(wcForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      plantCode: d?.plantCode ?? row.plantCode ?? '',
      lineCode: d?.lineCode ?? row.lineCode ?? '',
      workshopCode: row.workshopCode ?? '',
      defaultCalendarCode: d?.defaultCalendarCode ?? '',
      capacityMinutesPerDay: String(d?.capacityMinutesPerDay ?? row.capacityMinutesPerDay ?? WORK_CENTER_DEFAULTS.capacityMinutesPerDay),
    })
  }
  finally {
    wcEditLoading.value = false
  }
}
async function submitWorkCenter() {
  if (!canCreateWorkCenter.value) {
    wcShowErrors.value = true
    return
  }
  const workshopCode = wcForm.workshopCode.trim()
  if (wcEditingCode.value) {
    await wcActions.update(wcEditingCode.value, {
      name: wcForm.name.trim(),
      plantCode: wcForm.plantCode.trim(),
      lineCode: wcForm.lineCode.trim(),
      defaultCalendarCode: wcForm.defaultCalendarCode.trim(),
      capacityMinutesPerDay: Number(wcForm.capacityMinutesPerDay) || WORK_CENTER_DEFAULTS.capacityMinutesPerDay,
      capacityUnit: WORK_CENTER_DEFAULTS.capacityUnit,
      finiteCapacity: WORK_CENTER_DEFAULTS.finiteCapacity,
      workshopCode: workshopCode || null,
    })
    toast.success(`工作中心「${wcForm.name.trim()}」已更新。`)
  }
  else {
    await workCenters.create({
      organizationId: workCenters.filters.organizationId,
      environmentId: workCenters.filters.environmentId,
      code: wcForm.code.trim(),
      name: wcForm.name.trim(),
      plantCode: wcForm.plantCode.trim(),
      lineCode: wcForm.lineCode.trim(),
      defaultCalendarCode: wcForm.defaultCalendarCode.trim(),
      capacityMinutesPerDay: Number(wcForm.capacityMinutesPerDay) || WORK_CENTER_DEFAULTS.capacityMinutesPerDay,
      resourceType: WORK_CENTER_DEFAULTS.resourceType,
      capacityUnit: WORK_CENTER_DEFAULTS.capacityUnit,
      finiteCapacity: WORK_CENTER_DEFAULTS.finiteCapacity,
      ...(workshopCode ? { workshopCode } : {}),
    })
    toast.success(`工作中心「${wcForm.name.trim()}」已创建。`)
  }
  resetWcForm()
  wcEditingCode.value = null
  wcShowErrors.value = false
  wcOpen.value = false
}

function filterRows(items: BusinessConsoleResourceItem[], keyword: string) {
  const kw = keyword.trim().toLowerCase()
  if (!kw) return items
  return items.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
}

function refreshAll() {
  void sites.refresh()
  void workshops.refreshWorkshops()
  void lines.refresh()
  void workCenters.refresh()
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="工厂与产线" :breadcrumbs="[{ label: '基础数据' }]" :count="`${sites.total.value} 个工厂`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="sites.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">层级：工厂 → 车间 → 产线 → 工作中心 → 设备</p>

    <SectionCards :columns="4">
      <SectionCard description="工厂数" :value="sites.total.value" hint="生产站点" />
      <SectionCard description="车间数" :value="workshops.workshopsTotal.value" hint="工厂下组织层" />
      <SectionCard description="产线数" :value="lines.total.value" hint="所属各工厂" />
      <SectionCard description="工作中心数" :value="workCenters.total.value" hint="产能资源" />
    </SectionCards>

    <Tabs default-value="site">
      <TabsList>
        <TabsTrigger value="site">工厂 ({{ sites.total.value }})</TabsTrigger>
        <TabsTrigger value="line">产线 ({{ lines.total.value }})</TabsTrigger>
        <TabsTrigger value="workshop">车间 ({{ workshops.workshopsTotal.value }})</TabsTrigger>
        <TabsTrigger value="work-center">工作中心 ({{ workCenters.total.value }})</TabsTrigger>
      </TabsList>

      <!-- 工厂 -->
      <TabsContent value="site" class="grid gap-3">
        <Toolbar v-model:search="siteKeyword" search-placeholder="在当前页内筛选工厂编码、名称">
          <template #actions>
            <Dialog v-model:open="siteOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateSite">
                  <PlusIcon aria-hidden="true" />
                  新建工厂
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ siteEditingCode ? `编辑工厂 · ${siteEditingCode}` : '新建工厂' }}</DialogTitle>
                  <DialogDescription>{{ siteEditingCode ? '修改工厂档案（编码不可修改）。带 * 为必填项。' : '登记一个生产站点。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitSite">
                  <p v-if="siteCreateError" class="text-sm text-destructive" role="alert">{{ siteCreateError }}</p>
                  <p v-if="siteShowErrors && !canCreateSite" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="siteShowErrors && !isNonEmpty(siteForm.code)">
                      <FieldLabel for="site-code">工厂编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="site-code" v-model="siteForm.code" autocomplete="off" :disabled="!!siteEditingCode" required />
                    </Field>
                    <Field :data-invalid="siteShowErrors && !isNonEmpty(siteForm.name)">
                      <FieldLabel for="site-name">工厂名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="site-name" v-model="siteForm.name" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="siteShowErrors && !isNonEmpty(siteForm.timezone)">
                      <FieldLabel for="site-tz">时区 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="site-tz" v-model="siteForm.timezone" autocomplete="off" required />
                      <FieldDescription>如 Asia/Shanghai，用于排程与报表的本地时间。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="siteOpen = false">取消</Button>
                    <Button type="submit" :disabled="sites.createPending.value || siteActions.updatePending.value || siteEditLoading">
                      <Spinner v-if="sites.createPending.value || siteActions.updatePending.value" aria-hidden="true" />
                      {{ siteEditingCode ? '保存修改' : '保存工厂' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="siteListError" class="text-sm text-destructive" role="alert">{{ siteListError }}</p>
        <p v-else-if="facilityActionError" class="text-sm text-destructive" role="alert">{{ facilityActionError }}</p>
        <DataTable
          :columns="columns"
          :rows="siteRows"
          :row-key="rowKey"
          :loading="sites.pending.value"
          empty-message="暂无工厂。可清空筛选或新建工厂。"
        >
          <template #cell-active="{ row }">
            <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="工厂" :detail-fields="siteDetailFields(row)" :actions="siteActions" @edit="openEditSite" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="sitePage" v-model:page-size="sitePageSize" :total-items="sites.total.value" />
      </TabsContent>

      <!-- 产线 -->
      <TabsContent value="line" class="grid gap-3">
        <Toolbar v-model:search="lineKeyword" search-placeholder="在当前页内筛选产线编码、名称">
          <template #actions>
            <Dialog v-model:open="lineOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateLine">
                  <PlusIcon aria-hidden="true" />
                  新建产线
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ lineEditingCode ? `编辑产线 · ${lineEditingCode}` : '新建产线' }}</DialogTitle>
                  <DialogDescription>{{ lineEditingCode ? '修改产线档案（编码不可修改）。带 * 为必填项。' : '在所属工厂下登记一条产线。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitLine">
                  <p v-if="lineCreateError" class="text-sm text-destructive" role="alert">{{ lineCreateError }}</p>
                  <p v-if="lineShowErrors && !canCreateLine" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="lineShowErrors && !isNonEmpty(lineForm.code)">
                      <FieldLabel for="line-code">产线编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="line-code" v-model="lineForm.code" autocomplete="off" :disabled="!!lineEditingCode" required />
                    </Field>
                    <Field :data-invalid="lineShowErrors && !isNonEmpty(lineForm.name)">
                      <FieldLabel for="line-name">产线名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="line-name" v-model="lineForm.name" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="lineShowErrors && !isNonEmpty(lineForm.siteCode)">
                      <FieldLabel for="line-site">所属工厂 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="lineForm.siteCode">
                        <SelectTrigger id="line-site"><SelectValue placeholder="请选择工厂" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? ''">
                            {{ s.displayName ?? s.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription>缺少工厂？先到「工厂」页新建。</FieldDescription>
                    </Field>
                    <Field>
                      <FieldLabel for="line-workshop">所属车间</FieldLabel>
                      <Select v-model="lineForm.workshopCode">
                        <SelectTrigger id="line-workshop"><SelectValue placeholder="可选，请选择车间" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="w in workshops.workshops.value" :key="w.code" :value="w.code ?? ''">
                            {{ w.displayName ?? w.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription>可选。归属到工厂下的车间。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="lineOpen = false">取消</Button>
                    <Button type="submit" :disabled="lines.createPending.value || lineActions.updatePending.value || lineEditLoading">
                      <Spinner v-if="lines.createPending.value || lineActions.updatePending.value" aria-hidden="true" />
                      {{ lineEditingCode ? '保存修改' : '保存产线' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="lineListError" class="text-sm text-destructive" role="alert">{{ lineListError }}</p>
        <DataTable
          :columns="columns"
          :rows="lineRows"
          :row-key="rowKey"
          :loading="lines.pending.value"
          empty-message="暂无产线。可清空筛选或新建产线。"
        >
          <template #cell-active="{ row }">
            <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="产线" :detail-fields="lineDetailFields(row)" :actions="lineActions" @edit="openEditLine" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="linePage" v-model:page-size="linePageSize" :total-items="lines.total.value" />
      </TabsContent>

      <!-- 车间（工厂下的组织 / 区域层，产线与工作中心可归属到车间） -->
      <TabsContent value="workshop" class="grid gap-3">
        <Toolbar v-model:search="workshopKeyword" search-placeholder="在当前页内筛选车间编码、名称">
          <template #actions>
            <Dialog v-model:open="workshopOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateWorkshop">
                  <PlusIcon aria-hidden="true" />
                  新建车间
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ workshopEditingCode ? `编辑车间 · ${workshopEditingCode}` : '新建车间' }}</DialogTitle>
                  <DialogDescription>{{ workshopEditingCode ? '修改车间档案（编码不可修改）。带 * 为必填项。' : '在所属工厂下登记一个车间。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitWorkshop">
                  <p v-if="workshopCreateError" class="text-sm text-destructive" role="alert">{{ workshopCreateError }}</p>
                  <p v-if="workshopShowErrors && !canCreateWorkshop" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="workshopShowErrors && !isNonEmpty(workshopForm.code)">
                      <FieldLabel for="workshop-code">车间编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="workshop-code" v-model="workshopForm.code" autocomplete="off" :disabled="!!workshopEditingCode" required />
                    </Field>
                    <Field :data-invalid="workshopShowErrors && !isNonEmpty(workshopForm.name)">
                      <FieldLabel for="workshop-name">车间名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="workshop-name" v-model="workshopForm.name" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="workshopShowErrors && !isNonEmpty(workshopForm.siteCode)">
                      <FieldLabel for="workshop-site">所属工厂 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="workshopForm.siteCode">
                        <SelectTrigger id="workshop-site"><SelectValue placeholder="请选择工厂" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? ''">
                            {{ s.displayName ?? s.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription>缺少工厂？先到「工厂」页新建。</FieldDescription>
                    </Field>
                    <Field>
                      <FieldLabel for="workshop-manager">负责人</FieldLabel>
                      <Input id="workshop-manager" v-model="workshopForm.managerUserId" autocomplete="off" />
                      <FieldDescription>可选，车间负责人。</FieldDescription>
                    </Field>
                    <Field class="sm:col-span-2">
                      <FieldLabel for="workshop-desc">说明</FieldLabel>
                      <Input id="workshop-desc" v-model="workshopForm.description" autocomplete="off" />
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="workshopOpen = false">取消</Button>
                    <Button type="submit" :disabled="workshops.createWorkshopPending.value || workshopActions.updatePending.value || workshopEditLoading">
                      <Spinner v-if="workshops.createWorkshopPending.value || workshopActions.updatePending.value" aria-hidden="true" />
                      {{ workshopEditingCode ? '保存修改' : '保存车间' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="workshopListError" class="text-sm text-destructive" role="alert">{{ workshopListError }}</p>
        <DataTable
          :columns="workshopColumns"
          :rows="workshopRows"
          :row-key="rowKey"
          :loading="workshops.workshopsPending.value"
          empty-message="暂无车间。可清空筛选或新建车间。"
        >
          <template #cell-siteCode="{ row }">
            {{ row.siteCode ?? '无' }}
          </template>
          <template #cell-active="{ row }">
            <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="车间" :detail-fields="workshopDetailFields(row)" :actions="workshopActions" @edit="openEditWorkshop" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="workshopPage" v-model:page-size="workshopPageSize" :total-items="workshops.workshopsTotal.value" />
      </TabsContent>

      <!-- 工作中心 -->
      <TabsContent value="work-center" class="grid gap-3">
        <Toolbar v-model:search="wcKeyword" search-placeholder="在当前页内筛选工作中心编码、名称">
          <template #actions>
            <Dialog v-model:open="wcOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateWorkCenter">
                  <PlusIcon aria-hidden="true" />
                  新建工作中心
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-2xl">
                <DialogHeader>
                  <DialogTitle>{{ wcEditingCode ? `编辑工作中心 · ${wcEditingCode}` : '新建工作中心' }}</DialogTitle>
                  <DialogDescription>{{ wcEditingCode ? '修改工作中心档案（编码不可修改）。带 * 为必填项。' : '在工厂与产线下登记一个产能资源。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitWorkCenter">
                  <p v-if="wcCreateError" class="text-sm text-destructive" role="alert">{{ wcCreateError }}</p>
                  <p v-if="wcShowErrors && !canCreateWorkCenter" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.code)">
                      <FieldLabel for="wc-code">工作中心编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-code" v-model="wcForm.code" autocomplete="off" :disabled="!!wcEditingCode" required />
                    </Field>
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.name)">
                      <FieldLabel for="wc-name">工作中心名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-name" v-model="wcForm.name" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.plantCode)">
                      <FieldLabel for="wc-plant">所属工厂 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="wcForm.plantCode">
                        <SelectTrigger id="wc-plant"><SelectValue placeholder="请选择工厂" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? ''">
                            {{ s.displayName ?? s.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.lineCode)">
                      <FieldLabel for="wc-line">所属产线 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="wcForm.lineCode">
                        <SelectTrigger id="wc-line"><SelectValue placeholder="请选择产线" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="l in lines.items.value" :key="l.code" :value="l.code ?? ''">
                            {{ l.displayName ?? l.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <FieldLabel for="wc-workshop">所属车间</FieldLabel>
                      <Select v-model="wcForm.workshopCode">
                        <SelectTrigger id="wc-workshop"><SelectValue placeholder="可选，请选择车间" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="w in workshops.workshops.value" :key="w.code" :value="w.code ?? ''">
                            {{ w.displayName ?? w.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription>可选。归属到工厂下的车间。</FieldDescription>
                    </Field>
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.defaultCalendarCode)">
                      <FieldLabel for="wc-cal">默认工作日历 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-cal" v-model="wcForm.defaultCalendarCode" autocomplete="off" required />
                      <FieldDescription>填写「组织与人员」页中已建工作日历的编码。</FieldDescription>
                    </Field>
                    <Field :data-invalid="wcShowErrors && !((Number(wcForm.capacityMinutesPerDay) || 0) > 0)">
                      <FieldLabel for="wc-cap">日产能（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-cap" v-model="wcForm.capacityMinutesPerDay" type="number" min="1" inputmode="numeric" />
                      <FieldDescription>单日可用产能分钟数，默认 480（8 小时）。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="wcOpen = false">取消</Button>
                    <Button type="submit" :disabled="workCenters.createPending.value || wcActions.updatePending.value || wcEditLoading">
                      <Spinner v-if="workCenters.createPending.value || wcActions.updatePending.value" aria-hidden="true" />
                      {{ wcEditingCode ? '保存修改' : '保存工作中心' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="wcListError" class="text-sm text-destructive" role="alert">{{ wcListError }}</p>
        <DataTable
          :columns="columns"
          :rows="wcRows"
          :row-key="rowKey"
          :loading="workCenters.pending.value"
          empty-message="暂无工作中心。可清空筛选或新建工作中心。"
        >
          <template #cell-active="{ row }">
            <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="工作中心" :detail-fields="workCenterDetailFields(row)" :actions="wcActions" @edit="openEditWorkCenter" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="wcPage" v-model:page-size="wcPageSize" :total-items="workCenters.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
