<script setup lang="ts">
import type { InstanceDetailResponse } from '@nerv-iip/api-client'
import { Card, CardContent, CardHeader, CardTitle, Separator, Skeleton, StatusBadge } from '@nerv-iip/ui'
import { computed } from 'vue'
import { instanceStatusLabel, instanceTone } from './instanceStatus'

const props = defineProps<{
  instance?: InstanceDetailResponse
  pending?: boolean
}>()

type Capability = NonNullable<InstanceDetailResponse['capabilities']>[number]

const metadataEntries = computed(() => Object.entries(props.instance?.metadata ?? {}))

function capabilityKey(capability: Capability, index: number) {
  const code = capability.capabilityCode ?? 'unknown'
  const version = capability.capabilityVersion ?? 'unversioned'
  return capability.capabilityCode && capability.capabilityVersion
    ? `capability:${code}:${version}`
    : `capability:${code}:${version}:${index}`
}
</script>

<template>
  <Card class="min-w-0" aria-labelledby="detail-panel-title">
    <CardHeader class="pb-3">
      <p class="text-xs font-bold uppercase tracking-wider text-primary">已选实例</p>
      <CardTitle id="detail-panel-title" class="text-lg">
        {{ instance?.instanceName ?? instance?.instanceKey ?? '实例详情' }}
      </CardTitle>
    </CardHeader>

    <Separator />

    <CardContent class="pt-4">
      <template v-if="pending">
        <div class="flex flex-col gap-3">
          <Skeleton v-for="i in 6" :key="i" class="h-4 w-full" />
        </div>
      </template>

      <template v-else-if="instance">
        <dl class="grid gap-3">
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">应用</dt>
            <dd class="break-anywhere text-sm">{{ instance.applicationName ?? instance.applicationKey ?? '未知' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">节点</dt>
            <dd class="break-anywhere text-sm">{{ instance.nodeName ?? instance.nodeKey ?? '未分配' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">状态</dt>
            <dd>
              <StatusBadge :label="instanceStatusLabel(instance.reportedStatus)" :tone="instanceTone(instance.reportedStatus)" />
            </dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">健康</dt>
            <dd>
              <StatusBadge :label="instanceStatusLabel(instance.healthStatus)" :tone="instanceTone(instance.healthStatus)" />
            </dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">最近心跳</dt>
            <dd class="text-sm">{{ instance.lastHeartbeatAtUtc ?? '未上报' }}</dd>
          </div>
          <div class="grid gap-0.5">
            <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">最近状态</dt>
            <dd class="text-sm">{{ instance.lastStateObservedAtUtc ?? '未上报' }}</dd>
          </div>
        </dl>

        <Separator class="my-4" />

        <section aria-labelledby="capabilities-title">
          <h3 id="capabilities-title" class="mb-3 text-sm font-semibold">能力</h3>
          <ul v-if="instance.capabilities?.length" class="flex flex-col gap-2 p-0 list-none m-0">
            <li
              v-for="(capability, index) in instance.capabilities"
              :key="capabilityKey(capability, index)"
              class="flex flex-col gap-0.5 rounded-md border bg-muted/40 p-3"
            >
              <span class="break-anywhere text-sm font-semibold">
                {{ capability.capabilityCode ?? '未知' }}
              </span>
              <span class="text-xs text-muted-foreground">{{ capability.category ?? '未分类' }}</span>
              <span class="text-xs text-muted-foreground">
                {{ capability.supportedOperations?.join('、') ?? '无可用操作' }}
              </span>
            </li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">暂无能力上报。</p>
        </section>

        <Separator class="my-4" />

        <section aria-labelledby="metadata-title">
          <h3 id="metadata-title" class="mb-3 text-sm font-semibold">元数据</h3>
          <dl v-if="metadataEntries.length" class="flex flex-col gap-2 m-0">
            <div
              v-for="[key, value] in metadataEntries"
              :key="key"
              class="grid gap-0.5"
            >
              <dt class="text-xs font-bold uppercase tracking-wider text-muted-foreground">{{ key }}</dt>
              <dd class="break-anywhere text-sm m-0">{{ value }}</dd>
            </div>
          </dl>
          <p v-else class="text-sm text-muted-foreground">暂无元数据。</p>
        </section>
      </template>

      <p v-else class="text-sm text-muted-foreground">
        选择一个实例以查看其运行时信息。
      </p>
    </CardContent>
  </Card>
</template>
