<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { useBusinessMasterDataGroups, type BusinessMasterDataGroupDefinition } from '@/composables/useBusinessMasterData'
import { demoResourceGroups } from '@/data/shockAbsorberDemo'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工厂资源' } })

type SortColumn = 'resourceType' | 'code' | 'displayName' | 'active' | 'snapshotVersion'

const resourceDefinitions: BusinessMasterDataGroupDefinition[] = [
  { key: 'site', title: '工厂' },
  { key: 'production-line', title: '产线' },
  { key: 'work-center', title: '工作中心' },
  { key: 'device-asset', title: '设备' },
  { key: 'shift', title: '班次' },
  { key: 'work-calendar', title: '工作日历' },
  { key: 'team', title: '班组' },
  { key: 'personnel-skill', title: '人员技能' },
]

const { groups, groupsError, groupsPending, refreshGroups } = useBusinessMasterDataGroups(resourceDefinitions)

const filterDraft = reactive({
  keyword: '',
  group: 'all',
  siteCode: 'all',
  lineCode: 'all',
  workCenterCode: 'all',
  effectiveDate: new Date().toISOString().slice(0, 10),
})
const appliedFilter = reactive({ ...filterDraft })
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'code' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const resourceTitleByKey = Object.fromEntries(resourceDefinitions.map((definition) => [definition.key, definition.title]))
const scenarioRelations: Record<string, { siteCode?: string; lineCode?: string; workCenterCode?: string; shiftCode?: string }> = {
  'LINE-FRT-A': { siteCode: 'PLANT-NB' },
  'LINE-RR-B': { siteCode: 'PLANT-NB' },
  'LINE-SPARE': { siteCode: 'PLANT-CQ' },
  'WC-TUBE-WELD': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A' },
  'WC-ROD-ASM': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A' },
  'WC-OIL-FILL': { siteCode: 'PLANT-NB', lineCode: 'LINE-RR-B' },
  'WC-DAMP-TEST': { siteCode: 'PLANT-NB', lineCode: 'LINE-RR-B' },
  'WC-PACK': { siteCode: 'PLANT-CQ', lineCode: 'LINE-SPARE' },
  'EQ-WELD-01': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A', workCenterCode: 'WC-TUBE-WELD' },
  'EQ-ROD-ASM-01': { siteCode: 'PLANT-NB', lineCode: 'LINE-FRT-A', workCenterCode: 'WC-ROD-ASM' },
  'EQ-FILL-02': { siteCode: 'PLANT-NB', lineCode: 'LINE-RR-B', workCenterCode: 'WC-OIL-FILL' },
  'EQ-TEST-01': { siteCode: 'PLANT-NB', lineCode: 'LINE-RR-B', workCenterCode: 'WC-DAMP-TEST' },
  'TEAM-FRT-DAY': { shiftCode: 'SHIFT-DAY' },
  'TEAM-RR-NIGHT': { shiftCode: 'SHIFT-NIGHT' },
}
const allRows = computed(() => {
  const gatewayRows = groups.value.flatMap((group) =>
    group.rows.map((row) => ({
      ...row,
      groupTitle: resourceTitleByKey[group.key] ?? group.title,
    })),
  )
  const scenarioRows = demoResourceGroups.flatMap((group) =>
    group.rows.map((row) => ({
      ...row,
      groupTitle: resourceTitleByKey[group.key] ?? group.title,
    })),
  )

  return mergeResourceRows([...gatewayRows, ...scenarioRows])
})
const siteOptions = computed(() => optionsFor('site'))
const lineOptions = computed(() => optionsFor('production-line'))
const workCenterOptions = computed(() => optionsFor('work-center'))
const listRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()

  return allRows.value.filter((row) => {
    const groupMatched = appliedFilter.group === 'all' || row.resourceType === appliedFilter.group
    const keywordMatched =
      !keyword ||
      [row.resourceType, row.code, row.displayName, row.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))

    return groupMatched && keywordMatched && linkedSelectorMatched(row)
  })
})
const sortedRows = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1
  return [...listRows.value].sort((left, right) =>
    String(left[tableState.sortBy] ?? '').localeCompare(String(right[tableState.sortBy] ?? ''), 'zh-Hans-CN') * direction,
  )
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const pagedRows = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})

watch(
  () => [
    appliedFilter.keyword,
    appliedFilter.group,
    appliedFilter.siteCode,
    appliedFilter.lineCode,
    appliedFilter.workCenterCode,
    appliedFilter.effectiveDate,
    tableState.pageSize,
    allRows.value.length,
  ],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  Object.assign(appliedFilter, filterDraft)
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.group = 'all'
  filterDraft.siteCode = 'all'
  filterDraft.lineCode = 'all'
  filterDraft.workCenterCode = 'all'
  filterDraft.effectiveDate = new Date().toISOString().slice(0, 10)
  applyFilters()
}

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }
  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function rowKey(row: BusinessConsoleResourceItem, index: number) {
  return `${row.resourceType}:${row.code ?? index}`
}

function linkedSelectorMatched(row: BusinessConsoleResourceItem) {
  const relation = row.code ? scenarioRelations[row.code] : undefined

  if (appliedFilter.siteCode !== 'all') {
    if (row.resourceType === 'site') return row.code === appliedFilter.siteCode
    if (relation?.siteCode !== appliedFilter.siteCode) return false
  }

  if (appliedFilter.lineCode !== 'all') {
    if (row.resourceType === 'production-line') return row.code === appliedFilter.lineCode
    if (relation?.lineCode !== appliedFilter.lineCode) return false
  }

  if (appliedFilter.workCenterCode !== 'all') {
    if (row.resourceType === 'work-center') return row.code === appliedFilter.workCenterCode
    if (relation?.workCenterCode !== appliedFilter.workCenterCode) return false
  }

  return true
}

function optionsFor(resourceType: string) {
  return allRows.value
    .filter((row) => row.resourceType === resourceType)
    .map((row) => ({ code: row.code, label: row.displayName }))
    .filter((row): row is { code: string; label: string } => Boolean(row.code && row.label))
}

function mergeResourceRows(rows: (BusinessConsoleResourceItem & { groupTitle: string })[]) {
  const seen = new Set<string>()
  const result: (BusinessConsoleResourceItem & { groupTitle: string })[] = []

  for (const row of rows) {
    const key = `${row.resourceType}:${row.code}`
    if (seen.has(key)) continue
    seen.add(key)
    result.push(row)
  }

  return result
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="主数据"
        title="工厂资源"
        summary="维护工厂、产线、工作中心、设备和班次，支撑生产计划、派工、报工和产能判断。"
      >
        <template #actions>
          <Button size="sm" variant="outline" type="button" :disabled="groupsPending" @click="refreshGroups">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_180px_180px_180px_180px_auto]">
            <Field>
              <FieldLabel for="resource-keyword">关键字</FieldLabel>
              <Input id="resource-keyword" v-model="filterDraft.keyword" placeholder="编码、名称、版本" @keydown.enter="applyFilters" />
            </Field>
            <Field>
              <FieldLabel for="resource-type">资源类型</FieldLabel>
              <Select v-model="filterDraft.group">
                <SelectTrigger id="resource-type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部</SelectItem>
                  <SelectItem v-for="group in demoResourceGroups" :key="group.key" :value="group.key">
                    {{ group.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="resource-site">工厂</FieldLabel>
              <Select v-model="filterDraft.siteCode">
                <SelectTrigger id="resource-site">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部工厂</SelectItem>
                  <SelectItem v-for="site in siteOptions" :key="site.code" :value="site.code">
                    {{ site.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="resource-line">产线</FieldLabel>
              <Select v-model="filterDraft.lineCode">
                <SelectTrigger id="resource-line">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部产线</SelectItem>
                  <SelectItem v-for="line in lineOptions" :key="line.code" :value="line.code">
                    {{ line.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="resource-work-center">工作中心</FieldLabel>
              <Select v-model="filterDraft.workCenterCode">
                <SelectTrigger id="resource-work-center">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部工作中心</SelectItem>
                  <SelectItem v-for="workCenter in workCenterOptions" :key="workCenter.code" :value="workCenter.code">
                    {{ workCenter.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="resource-effective-date">有效日期</FieldLabel>
              <Input id="resource-effective-date" v-model="filterDraft.effectiveDate" type="date" />
            </Field>
            <div class="flex items-end gap-2">
              <Button type="button" @click="applyFilters">查询</Button>
              <Button type="button" variant="outline" @click="clearFilters">清空</Button>
            </div>
          </FieldGroup>
          <p v-if="formatError(groupsError)" class="mt-3 text-sm text-destructive">{{ formatError(groupsError) }}</p>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">资源列表</h2>
          <span class="flex items-center gap-2 text-sm text-muted-foreground">
            <Spinner v-if="groupsPending" class="size-4" />
            工厂 / 产线 / 工作中心 / 设备 / 班组
          </span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('resourceType')">
                    类型
                    <component :is="sortIcon('resourceType')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('code')">
                    编码
                    <component :is="sortIcon('code')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('displayName')">
                    名称
                    <component :is="sortIcon('displayName')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('active')">
                    状态
                    <component :is="sortIcon('active')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('snapshotVersion')">
                    版本
                    <component :is="sortIcon('snapshotVersion')" data-icon="inline-end" />
                  </Button>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(row, index) in pagedRows" :key="rowKey(row, index)">
                <TableCell>{{ row.groupTitle }}</TableCell>
                <TableCell class="font-medium">{{ row.code }}</TableCell>
                <TableCell>{{ row.displayName }}</TableCell>
                <TableCell>
                  <Badge :variant="row.active === false ? 'secondary' : 'success'">
                    {{ row.active === false ? '停用' : '启用' }}
                  </Badge>
                </TableCell>
                <TableCell>{{ row.snapshotVersion }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!listRows.length" :colspan="5">未找到资源。</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <BusinessTablePagination
            v-model:page="tableState.page"
            v-model:page-size="tableState.pageSize"
            :total-items="sortedRows.length"
          />
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
