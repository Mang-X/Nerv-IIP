<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { demoProductionFacts } from '@/data/shockAbsorberDemo'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge, Button, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工艺与版本' } })

type SortColumn = 'code' | 'name' | 'bom' | 'routing' | 'status'

const filterDraft = reactive({ keyword: '' })
const appliedFilter = reactive({ keyword: '' })
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'code' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const listRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()
  return demoProductionFacts.filter((row) =>
    !keyword ||
    [row.code, row.name, row.bom, row.routing, row.status]
      .some((value) => value.toLowerCase().includes(keyword)),
  )
})
const sortedRows = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1
  return [...listRows.value].sort((left, right) =>
    String(left[tableState.sortBy]).localeCompare(String(right[tableState.sortBy]), 'zh-Hans-CN') * direction,
  )
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const pagedRows = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})

watch(
  () => [appliedFilter.keyword, tableState.pageSize],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
}

function clearFilters() {
  filterDraft.keyword = ''
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
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="主数据"
        title="工艺与版本"
        summary="展示减振器量产所需的生产版本、BOM 和工艺路线样例，供生产计划和工单释放校验。"
      />

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
            <Field>
              <FieldLabel for="process-keyword">关键字</FieldLabel>
              <Input id="process-keyword" v-model="filterDraft.keyword" placeholder="版本、BOM、工艺路线" @keydown.enter="applyFilters" />
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
          <h2 class="text-sm font-semibold text-foreground">版本与路线</h2>
          <span class="text-sm text-muted-foreground">汽车减振器制造样例</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('code')">
                    编码
                    <component :is="sortIcon('code')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('name')">
                    名称
                    <component :is="sortIcon('name')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('bom')">
                    BOM / 工序
                    <component :is="sortIcon('bom')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('routing')">
                    路线
                    <component :is="sortIcon('routing')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('status')">
                    状态
                    <component :is="sortIcon('status')" data-icon="inline-end" />
                  </Button>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in pagedRows" :key="row.code">
                <TableCell class="font-medium">{{ row.code }}</TableCell>
                <TableCell>{{ row.name }}</TableCell>
                <TableCell>{{ row.bom }}</TableCell>
                <TableCell>{{ row.routing }}</TableCell>
                <TableCell>
                  <Badge variant="success">{{ row.status }}</Badge>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!listRows.length" :colspan="5">未找到工艺或生产版本。</TableEmpty>
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
