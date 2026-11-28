<script setup lang="ts">
import { instanceStatusLabel, instanceTone } from '@/components/console/instanceStatus'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { consolePagePermissions } from '@/router/access'
import { useAuthStore } from '@/stores/auth'
import { listConsoleInstances, type InstanceListItem } from '@nerv-iip/api-client'
import { Button, NvPageHeader, NvStatusBadge } from '@nerv-iip/ui'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    requiredPermissions: consolePagePermissions['/business'],
    title: 'routes.business',
  },
})

// Keys match the deployed Docker images reported to AppHub by the connector.
const serviceNames = {
  'business-master-data': '基础主数据',
  'business-product-engineering': '产品工程',
  'business-inventory': '库存',
  'business-quality': '质量',
  'business-mes': '生产执行',
  'business-demand-planning': '需求计划',
  'business-barcode-label': '条码标签',
  'business-approval': '业务审批',
  'business-wms': '仓储',
  'business-industrial-telemetry': '工业遥测',
  'business-maintenance': '设备维保',
  'business-erp': 'ERP',
}

const auth = useAuthStore()
const { data, error, isLoading, refetch } = useQuery(() => {
  const principal = auth.principal
  const organizationId = principal?.organizationId ?? ''
  const environmentId = principal?.environmentId ?? ''
  return {
    key: [
      'business-service-instances',
      principal?.principalId ?? '',
      organizationId,
      environmentId,
    ],
    enabled: Boolean(organizationId && environmentId),
    async query({ signal }) {
      const pageSize = 100
      const maxPages = 5
      const { data: firstEnvelope } = await listConsoleInstances({
        query: { organizationId, environmentId, pageIndex: 1, pageSize },
        signal,
        throwOnError: true,
      })
      if (!firstEnvelope?.success || !firstEnvelope.data) {
        throw new Error('无法读取当前组织环境的实例。')
      }
      const firstPage = firstEnvelope.data.items ?? []
      const byKey = new Map<string, InstanceListItem>()
      for (const item of firstPage) {
        if (item.instanceKey) byKey.set(item.instanceKey, item)
      }
      const totalCount = firstEnvelope.data.totalCount ?? firstPage.length
      const effectivePageSize = firstPage.length > 0 ? firstPage.length : pageSize
      const totalPages = Math.min(Math.ceil(totalCount / effectivePageSize), maxPages)
      if (totalPages > 1) {
        const extraEnvelopes = await Promise.all(
          Array.from({ length: totalPages - 1 }, (_, i) =>
            listConsoleInstances({
              query: { organizationId, environmentId, pageIndex: i + 2, pageSize },
              signal,
              throwOnError: true,
            }),
          ),
        )
        for (const { data: envelope } of extraEnvelopes) {
          for (const item of envelope?.data?.items ?? []) {
            if (item.instanceKey) byKey.set(item.instanceKey, item)
          }
        }
      }
      return Array.from(byKey.values())
    },
  }
})
const services = computed(() => {
  const byService = Map.groupBy(data.value ?? [], (row) => row.applicationKey)
  return Object.entries(serviceNames).map(([key, name]) => ({
    key,
    name,
    instances: byService.get(key) ?? [],
  }))
})
const businessConsoleUrl = import.meta.env.VITE_NERV_IIP_BUSINESS_CONSOLE_URL?.trim()

function refresh() {
  void refetch().catch(() => {})
}
</script>

<template>
  <DefaultLayout>
    <section class="grid min-w-0 gap-4">
      <NvPageHeader title="业务平台状态" :breadcrumbs="[{ label: '平台' }]">
        <template #actions>
          <Button type="button" variant="outline" :disabled="isLoading" @click="refresh"
            >刷新</Button
          >
          <Button v-if="businessConsoleUrl" as-child>
            <a :href="businessConsoleUrl">进入业务控制台</a>
          </Button>
        </template>
      </NvPageHeader>
      <p class="text-sm text-muted-foreground">
        {{ auth.principal?.organizationId }} · {{ auth.principal?.environmentId }}
      </p>
      <p v-if="error" role="alert" class="text-sm text-destructive">无法读取业务服务实例。</p>
      <p v-else-if="isLoading" role="status" class="text-sm">正在读取业务服务实例。</p>
      <dl v-else-if="data" class="divide-y rounded-md border">
        <div
          v-for="service in services"
          :key="service.key"
          class="grid gap-2 p-3 sm:grid-cols-[12rem_minmax(0,1fr)]"
        >
          <dt class="text-sm font-medium">{{ service.name }}</dt>
          <dd v-if="!service.instances.length"><NvStatusBadge label="未观测" tone="neutral" /></dd>
          <dd v-else class="grid gap-2">
            <div
              v-for="instance in service.instances"
              :key="instance.instanceKey"
              class="flex flex-wrap items-center gap-2 text-sm"
            >
              <RouterLink
                v-if="instance.instanceKey"
                :to="{ path: '/', query: { instance: instance.instanceKey } }"
                class="break-all text-primary underline"
              >
                {{ instance.instanceName || instance.instanceKey }}
              </RouterLink>
              <NvStatusBadge
                :label="
                  instance.reportedStatus ? instanceStatusLabel(instance.reportedStatus) : '未观测'
                "
                :tone="instanceTone(instance.reportedStatus)"
              />
              <NvStatusBadge
                :label="
                  instance.healthStatus ? instanceStatusLabel(instance.healthStatus) : '未观测'
                "
                :tone="instanceTone(instance.healthStatus)"
              />
            </div>
          </dd>
        </div>
      </dl>
    </section>
  </DefaultLayout>
</template>
