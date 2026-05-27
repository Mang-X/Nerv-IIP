<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
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
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工厂资源' } })

type SortColumn = 'resourceType' | 'code' | 'displayName' | 'active' | 'snapshotVersion'

const filterDraft = reactive({ keyword: '', group: 'all' })
const appliedFilter = reactive({ keyword: '', group: 'all' })
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'code' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const allRows = computed(() =>
  demoResourceGroups.flatMap((group) =>
    group.rows.map((row) => ({ ...row, groupTitle: group.title })),
  ),
)
const listRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()

  return allRows.value.filter((row) => {
    const groupMatched = appliedFilter.group === 'all' || row.resourceType === appliedFilter.group
    const keywordMatched =
      !keyword ||
      [row.resourceType, row.code, row.displayName, row.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))

    return groupMatched && keywordMatched
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
  () => [appliedFilter.keyword, appliedFilter.group, tableState.pageSize],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
  appliedFilter.group = filterDraft.group
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.group = 'all'
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
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="主数据"
        title="工厂资源"
        summary="减振器工厂的工厂、产线、工作中心、设备和班次样例，用于生产计划、派工和报工联动。"
      />

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]">
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
            <div class="flex items-end gap-2">
              <Button type="button" @click="applyFilters">查询</Button>
              <Button type="button" variant="outline" @click="clearFilters">清空</Button>
            </div>
          </FieldGroup>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">资源列表</h2>
          <span class="text-sm text-muted-foreground">汽车减振器制造样例</span>
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
