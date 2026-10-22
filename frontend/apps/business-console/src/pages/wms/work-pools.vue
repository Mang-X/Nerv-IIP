<script setup lang="ts">
import type { BusinessConsoleWmsWorkPool } from '@nerv-iip/api-client'
import type { EntityPickerOption, NvDataTableColumn } from '@nerv-iip/ui'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import {
  useBusinessMasterDataResources,
  useBusinessWorkers,
} from '@/composables/useBusinessMasterData'
import { useWmsWorkPools } from '@/composables/useWmsWorkPools'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon, UsersIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '作业池',
    requiredPermissions: ['business.wms.work-pools.manage'],
  },
})

const {
  pools,
  poolsPending,
  poolsError,
  refresh,
  createPool,
  createPoolPending,
  addMember,
  addMemberPending,
  removeMember,
  removeMemberPending,
  newIdempotencyKey,
} = useWmsWorkPools()

// 工厂与人员一律按主数据显示中文名，编码只作绑定值。
const siteCatalog = useBusinessMasterDataResources('site')
siteCatalog.filters.take = 500
const siteOptions = computed<EntityPickerOption[]>(() =>
  siteCatalog.resources.value.flatMap((site) =>
    site.code ? [{ value: site.code, label: site.displayName || site.code }] : [],
  ),
)
function siteName(code?: string | null) {
  if (!code) return '—'
  return siteOptions.value.find((option) => option.value === code)?.label ?? code
}
const { workers } = useBusinessWorkers({ pageSize: 500 })
function workerName(userId?: string | null) {
  const worker = workers.value.find((candidate) => candidate.userId === userId)
  if (!worker) return '未知人员'
  return worker.employeeNo
    ? `${worker.displayName || '未命名人员'}（${worker.employeeNo}）`
    : worker.displayName || '未命名人员'
}

type PoolRow = BusinessConsoleWmsWorkPool
const columns: NvDataTableColumn<PoolRow>[] = [
  { key: 'displayName', header: '作业池', cellClass: 'font-medium' },
  { key: 'poolCode', header: '编码', width: 'w-32' },
  { key: 'siteCode', header: '工厂', accessor: (r) => siteName(r.siteCode) },
  { key: 'members', header: '成员' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]
const headerCount = computed(() =>
  poolsPending.value ? '加载中' : `${pools.value.length} 个作业池`,
)

// —— 新建作业池：编码由系统生成，只填名称与工厂 ——
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createKey = shallowRef('')
const createForm = reactive({ displayName: '', siteCode: '' })
function openCreate() {
  createForm.displayName = ''
  createForm.siteCode = siteOptions.value.length === 1 ? siteOptions.value[0]!.value : ''
  createError.value = ''
  createKey.value = newIdempotencyKey()
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.displayName.trim() || !createForm.siteCode.trim()) {
    createError.value = '请填写作业池名称并选择工厂。'
    return
  }
  try {
    const created = await createPool({
      displayName: createForm.displayName.trim(),
      siteCode: createForm.siteCode.trim(),
      idempotencyKey: createKey.value,
    })
    createOpen.value = false
    notifySuccess(`作业池 ${created.data?.poolCode ?? ''} 已创建`)
  } catch (error) {
    notifyOperationFailure('新建作业池失败', error, '新建作业池失败，请稍后重试。')
  }
}

// —— 成员维护 ——
const membersPoolCode = shallowRef('')
const membersPool = computed(() =>
  pools.value.find((pool) => pool.poolCode === membersPoolCode.value),
)
const selectedWorker = shallowRef('')
function openMembers(row: PoolRow) {
  membersPoolCode.value = row.poolCode ?? ''
  selectedWorker.value = ''
}
async function submitAddMember() {
  const poolCode = membersPoolCode.value
  if (!poolCode || !selectedWorker.value) return
  try {
    await addMember(poolCode, selectedWorker.value)
    selectedWorker.value = ''
    notifySuccess('已加入作业池')
  } catch (error) {
    notifyOperationFailure('加入成员失败', error, '加入成员失败，请稍后重试。')
  }
}
async function submitRemoveMember(principalId?: string | null) {
  const poolCode = membersPoolCode.value
  if (!poolCode || !principalId) return
  try {
    await removeMember(poolCode, principalId)
    notifySuccess('已移出作业池')
  } catch (error) {
    notifyOperationFailure('移出成员失败', error, '移出成员失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="作业池" :breadcrumbs="[{ label: '仓储作业' }]" :count="headerCount">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="poolsPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建作业池
        </NvButton>
      </template>
    </NvPageHeader>

    <NvDataTable
      :columns="columns"
      :rows="pools"
      :row-key="(row: PoolRow) => row.poolCode ?? ''"
      :loading="poolsPending"
      :error="poolsError"
      error-message="作业池列表取不到，请重试。"
      :searchable="false"
      :column-settings="false"
      empty-message="还没有作业池。"
      @retry="refresh"
    >
      <template #empty>
        <p class="text-sm font-medium">还没有作业池</p>
        <p class="max-w-md text-sm text-muted-foreground">
          入库、出库、盘点单要先分配到作业池，池内成员才能在现场执行。
        </p>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建作业池
        </NvButton>
      </template>
      <template #cell-members="{ row }">
        <span v-if="!row.members?.length" class="text-muted-foreground">暂无成员</span>
        <span v-else>{{ row.members.map((m) => workerName(m.principalId)).join('、') }}</span>
      </template>
      <template #cell-actions="{ row }">
        <NvButton size="sm" type="button" variant="outline" @click="openMembers(row)">
          <UsersIcon aria-hidden="true" />
          管理成员
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建作业池</NvDialogTitle>
          <NvDialogDescription class="sr-only">作业池编码由系统生成。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="wms-pool-name">名称</NvFieldLabel>
              <NvInput
                id="wms-pool-name"
                v-model="createForm.displayName"
                autocomplete="off"
                placeholder="如 收货组"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-pool-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="wms-pool-site"
                v-model="createForm.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择工厂"
                empty-text="暂无工厂主数据，请先在基础数据维护工厂"
                :loading="siteCatalog.resourcesPending.value"
                aria-label="工厂"
              />
            </NvField>
            <NvFieldError v-if="createError" :errors="[createError]" />
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createPoolPending">创建</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog
      :open="Boolean(membersPoolCode)"
      @update:open="
        (v) => {
          if (!v) membersPoolCode = ''
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>{{ membersPool?.displayName ?? '作业池' }} 的成员</NvDialogTitle>
          <NvDialogDescription class="sr-only">加入或移出作业池成员。</NvDialogDescription>
        </NvDialogHeader>
        <ul class="grid gap-2">
          <li v-if="!membersPool?.members?.length" class="text-sm text-muted-foreground">
            暂无成员
          </li>
          <li
            v-for="member in membersPool?.members ?? []"
            :key="member.principalId ?? ''"
            class="flex items-center justify-between rounded-lg border px-3 py-2"
          >
            <span class="text-sm">{{ workerName(member.principalId) }}</span>
            <NvButton
              size="sm"
              type="button"
              variant="ghost"
              :disabled="removeMemberPending"
              :aria-label="`移出 ${workerName(member.principalId)}`"
              @click="submitRemoveMember(member.principalId)"
            >
              <Trash2Icon aria-hidden="true" />
              移出
            </NvButton>
          </li>
        </ul>
        <form class="grid gap-3" @submit.prevent="submitAddMember">
          <NvField>
            <NvFieldLabel for="wms-pool-member">加入成员</NvFieldLabel>
            <WorkerSelect
              id="wms-pool-member"
              v-model="selectedWorker"
              placeholder="搜索并选择人员"
            />
          </NvField>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">关闭</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="addMemberPending || !selectedWorker">加入</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
