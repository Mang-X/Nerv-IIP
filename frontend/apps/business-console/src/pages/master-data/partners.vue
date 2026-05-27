<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { useBusinessMasterDataGroups } from '@/composables/useBusinessMasterData'
import { demoPartners } from '@/data/shockAbsorberDemo'
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

definePage({ meta: { requiresAuth: true, title: '客户与供应商' } })

type PartnerRole = 'all' | 'customer' | 'supplier'
type SortColumn = 'code' | 'displayName' | 'role' | 'active' | 'snapshotVersion'

const { groups, groupsError, groupsPending, refreshGroups } = useBusinessMasterDataGroups([
  { key: 'business-partner', title: '客户与供应商' },
])

const filterDraft = reactive({ keyword: '', role: 'all' as PartnerRole })
const appliedFilter = reactive({ keyword: '', role: 'all' as PartnerRole })
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'code' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})

const gatewayPartners = computed(() => {
  return groups.value.flatMap((group) =>
    group.rows.map((row) => ({
      ...row,
      role: inferPartnerRole(row),
    })),
  )
})
const allRows = computed(() => mergePartners([...gatewayPartners.value, ...demoPartners]))
const listRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()

  return allRows.value.filter((row) => {
    const roleMatched = appliedFilter.role === 'all' || row.role === appliedFilter.role
    const keywordMatched =
      !keyword ||
      [row.code, row.displayName, roleLabel(row.role), row.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))

    return roleMatched && keywordMatched
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
  () => [appliedFilter.keyword, appliedFilter.role, tableState.pageSize, allRows.value.length],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
  appliedFilter.role = filterDraft.role
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.role = 'all'
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

function rowKey(row: BusinessConsoleResourceItem & { role: PartnerRole }, index: number) {
  return `${row.resourceType}:${row.code ?? index}`
}

function inferPartnerRole(row: BusinessConsoleResourceItem): PartnerRole {
  const code = row.code?.toLowerCase() ?? ''
  if (code.includes('cust')) return 'customer'
  if (code.includes('sup')) return 'supplier'
  return 'all'
}

function roleLabel(role: PartnerRole) {
  if (role === 'customer') return '客户'
  if (role === 'supplier') return '供应商'
  return '未分配'
}

function mergePartners(rows: (BusinessConsoleResourceItem & { role: PartnerRole })[]) {
  const seen = new Set<string>()
  const result: (BusinessConsoleResourceItem & { role: PartnerRole })[] = []

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
        title="客户与供应商"
        summary="维护客户、供应商和供需角色，支撑销售需求、采购建议、收货检验和生产齐套选择。"
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
          <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]">
            <Field>
              <FieldLabel for="partner-keyword">关键字</FieldLabel>
              <Input id="partner-keyword" v-model="filterDraft.keyword" placeholder="编码、名称、版本" @keydown.enter="applyFilters" />
            </Field>
            <Field>
              <FieldLabel for="partner-role">角色</FieldLabel>
              <Select v-model="filterDraft.role">
                <SelectTrigger id="partner-role">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部</SelectItem>
                  <SelectItem value="customer">客户</SelectItem>
                  <SelectItem value="supplier">供应商</SelectItem>
                </SelectContent>
              </Select>
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
          <h2 class="text-sm font-semibold text-foreground">伙伴列表</h2>
          <span class="flex items-center gap-2 text-sm text-muted-foreground">
            <Spinner v-if="groupsPending" class="size-4" />
            客户 / 供应商
          </span>
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
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('displayName')">
                    名称
                    <component :is="sortIcon('displayName')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('role')">
                    角色
                    <component :is="sortIcon('role')" data-icon="inline-end" />
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
                <TableCell class="font-medium">{{ row.code }}</TableCell>
                <TableCell>{{ row.displayName }}</TableCell>
                <TableCell>{{ roleLabel(row.role) }}</TableCell>
                <TableCell>
                  <Badge :variant="row.active === false ? 'secondary' : 'success'">
                    {{ row.active === false ? '停用' : '启用' }}
                  </Badge>
                </TableCell>
                <TableCell>{{ row.snapshotVersion }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!listRows.length && !groupsPending" :colspan="5">未找到客户或供应商。</TableEmpty>
              <TableEmpty v-if="groupsPending" :colspan="5">正在加载客户与供应商...</TableEmpty>
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
