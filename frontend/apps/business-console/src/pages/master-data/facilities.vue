<script setup lang="ts">
import type {
  BusinessConsoleCreateProductionLineRequest,
  BusinessConsoleCreateSiteRequest,
  BusinessConsoleCreateWorkCenterRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMasterDataResource } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
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
import { computed, reactive, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工厂与产线' } })

const DEFAULT_TIMEZONE = 'Asia/Shanghai'
const WORK_CENTER_DEFAULTS = {
  resourceType: 'work-center',
  capacityUnit: 'minutes',
  capacityMinutesPerDay: 480,
  finiteCapacity: true,
}

const sites = useMasterDataResource<BusinessConsoleCreateSiteRequest>('site')
const lines = useMasterDataResource<BusinessConsoleCreateProductionLineRequest>('production-line')
const workCenters = useMasterDataResource<BusinessConsoleCreateWorkCenterRequest>('work-center')

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
]

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

async function submitSite() {
  if (!canCreateSite.value) {
    siteShowErrors.value = true
    return
  }
  await sites.create({
    organizationId: sites.filters.organizationId,
    environmentId: sites.filters.environmentId,
    code: siteForm.code.trim(),
    name: siteForm.name.trim(),
    timezone: siteForm.timezone.trim(),
  })
  toast.success(`工厂「${siteForm.name.trim()}」已创建。`)
  Object.assign(siteForm, { code: '', name: '', timezone: DEFAULT_TIMEZONE })
  siteShowErrors.value = false
  siteOpen.value = false
}

// ---- 产线 ----
const lineKeyword = ref('')
const linePage = ref(1)
const linePageSize = ref('10')
const lineOpen = ref(false)
const lineShowErrors = ref(false)
const lineForm = reactive({ code: '', name: '', siteCode: '' })
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

async function submitLine() {
  if (!canCreateLine.value) {
    lineShowErrors.value = true
    return
  }
  await lines.create({
    organizationId: lines.filters.organizationId,
    environmentId: lines.filters.environmentId,
    code: lineForm.code.trim(),
    name: lineForm.name.trim(),
    siteCode: lineForm.siteCode.trim(),
  })
  toast.success(`产线「${lineForm.name.trim()}」已创建。`)
  Object.assign(lineForm, { code: '', name: '', siteCode: '' })
  lineShowErrors.value = false
  lineOpen.value = false
}

// ---- 工作中心 ----
const wcKeyword = ref('')
const wcPage = ref(1)
const wcPageSize = ref('10')
const wcOpen = ref(false)
const wcShowErrors = ref(false)
const wcForm = reactive({ code: '', name: '', plantCode: '', lineCode: '', defaultCalendarCode: '', capacityMinutesPerDay: '480' })
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

async function submitWorkCenter() {
  if (!canCreateWorkCenter.value) {
    wcShowErrors.value = true
    return
  }
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
  })
  toast.success(`工作中心「${wcForm.name.trim()}」已创建。`)
  Object.assign(wcForm, { code: '', name: '', plantCode: '', lineCode: '', defaultCalendarCode: '', capacityMinutesPerDay: '480' })
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

    <p class="text-sm text-muted-foreground">层级：工厂 → 车间（组织层·建设中） → 产线 → 工作中心 → 设备</p>

    <SectionCards :columns="3">
      <SectionCard description="工厂数" :value="sites.total.value" hint="生产站点" />
      <SectionCard description="产线数" :value="lines.total.value" hint="所属各工厂" />
      <SectionCard description="工作中心数" :value="workCenters.total.value" hint="产能资源" />
    </SectionCards>

    <Tabs default-value="site">
      <TabsList>
        <TabsTrigger value="site">工厂 ({{ sites.total.value }})</TabsTrigger>
        <TabsTrigger value="line">产线 ({{ lines.total.value }})</TabsTrigger>
        <TabsTrigger value="workshop">车间</TabsTrigger>
        <TabsTrigger value="work-center">工作中心 ({{ workCenters.total.value }})</TabsTrigger>
      </TabsList>

      <!-- 工厂 -->
      <TabsContent value="site" class="grid gap-3">
        <Toolbar v-model:search="siteKeyword" search-placeholder="在当前页内筛选工厂编码、名称">
          <template #actions>
            <Dialog v-model:open="siteOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button">
                  <PlusIcon aria-hidden="true" />
                  新建工厂
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建工厂</DialogTitle>
                  <DialogDescription>登记一个生产站点。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitSite">
                  <p v-if="siteCreateError" class="text-sm text-destructive" role="alert">{{ siteCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="siteShowErrors && !isNonEmpty(siteForm.code)">
                      <FieldLabel for="site-code">工厂编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="site-code" v-model="siteForm.code" autocomplete="off" required />
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
                    <Button type="submit" :disabled="sites.createPending.value">
                      <Spinner v-if="sites.createPending.value" aria-hidden="true" />
                      保存工厂
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="siteListError" class="text-sm text-destructive" role="alert">{{ siteListError }}</p>
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
        </DataTable>
        <DataTablePagination v-model:page="sitePage" v-model:page-size="sitePageSize" :total-items="sites.total.value" />
      </TabsContent>

      <!-- 产线 -->
      <TabsContent value="line" class="grid gap-3">
        <Toolbar v-model:search="lineKeyword" search-placeholder="在当前页内筛选产线编码、名称">
          <template #actions>
            <Dialog v-model:open="lineOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button">
                  <PlusIcon aria-hidden="true" />
                  新建产线
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建产线</DialogTitle>
                  <DialogDescription>在所属工厂下登记一条产线。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitLine">
                  <p v-if="lineCreateError" class="text-sm text-destructive" role="alert">{{ lineCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="lineShowErrors && !isNonEmpty(lineForm.code)">
                      <FieldLabel for="line-code">产线编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="line-code" v-model="lineForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="lineShowErrors && !isNonEmpty(lineForm.name)">
                      <FieldLabel for="line-name">产线名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="line-name" v-model="lineForm.name" autocomplete="off" required />
                    </Field>
                    <Field>
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
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="lineOpen = false">取消</Button>
                    <Button type="submit" :disabled="lines.createPending.value">
                      <Spinner v-if="lines.createPending.value" aria-hidden="true" />
                      保存产线
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
        </DataTable>
        <DataTablePagination v-model:page="linePage" v-model:page-size="linePageSize" :total-items="lines.total.value" />
      </TabsContent>

      <!-- 车间（组织层·建设中，后端尚无 Workshop 实体，本期仅占位预留，见 #348） -->
      <TabsContent value="workshop" class="grid gap-3">
        <Card>
          <CardHeader>
            <CardTitle class="text-base">车间（组织层·建设中）</CardTitle>
            <CardDescription>工厂下的组织 / 区域层</CardDescription>
          </CardHeader>
          <CardContent>
            <p class="text-sm text-muted-foreground">
              车间作为工厂下的组织 / 区域层，正在建设。建成后可在此维护车间，并将产线与工作中心归属到对应车间。
            </p>
          </CardContent>
        </Card>
      </TabsContent>

      <!-- 工作中心 -->
      <TabsContent value="work-center" class="grid gap-3">
        <Toolbar v-model:search="wcKeyword" search-placeholder="在当前页内筛选工作中心编码、名称">
          <template #actions>
            <Dialog v-model:open="wcOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button">
                  <PlusIcon aria-hidden="true" />
                  新建工作中心
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-2xl">
                <DialogHeader>
                  <DialogTitle>新建工作中心</DialogTitle>
                  <DialogDescription>在工厂与产线下登记一个产能资源。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitWorkCenter">
                  <p v-if="wcCreateError" class="text-sm text-destructive" role="alert">{{ wcCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.code)">
                      <FieldLabel for="wc-code">工作中心编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-code" v-model="wcForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.name)">
                      <FieldLabel for="wc-name">工作中心名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-name" v-model="wcForm.name" autocomplete="off" required />
                    </Field>
                    <Field>
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
                    <Field>
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
                    <Field :data-invalid="wcShowErrors && !isNonEmpty(wcForm.defaultCalendarCode)">
                      <FieldLabel for="wc-cal">默认工作日历 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-cal" v-model="wcForm.defaultCalendarCode" autocomplete="off" required />
                      <FieldDescription>填写「组织与人员」页中已建工作日历的编码。</FieldDescription>
                    </Field>
                    <Field>
                      <FieldLabel for="wc-cap">日产能（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="wc-cap" v-model="wcForm.capacityMinutesPerDay" type="number" min="1" inputmode="numeric" />
                      <FieldDescription>单日可用产能分钟数，默认 480（8 小时）。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="wcOpen = false">取消</Button>
                    <Button type="submit" :disabled="workCenters.createPending.value">
                      <Spinner v-if="workCenters.createPending.value" aria-hidden="true" />
                      保存工作中心
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
        </DataTable>
        <DataTablePagination v-model:page="wcPage" v-model:page-size="wcPageSize" :total-items="workCenters.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
