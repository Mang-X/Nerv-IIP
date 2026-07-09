<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvPageHeader, NvSectionCard, NvSectionCards } from '@nerv-iip/ui'

definePage({ meta: { requiresAuth: false, title: '设计系统 · T 型导航' } })
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="数字化工作台"
      :breadcrumbs="[{ label: '业务控制台', href: '#' }]"
      count="T 型导航预览"
    />
    <NvSectionCards>
      <NvSectionCard
        description="在制工单"
        :value="42"
        :trend="{ value: '+8.2%', direction: 'up' }"
        footnote="较上周稳步提升"
        hint="本周新开 12 单"
      />
      <NvSectionCard
        description="按时完工率"
        value="94.6%"
        :trend="{ value: '+1.4%', direction: 'up' }"
        footnote="高于目标 92%"
        hint="近 30 天"
      />
      <NvSectionCard
        description="待办审批"
        :value="7"
        :trend="{ value: '-2', direction: 'down' }"
        footnote="待处理减少"
        hint="审批中心"
      />
      <NvSectionCard
        description="设备预警"
        :value="3"
        :trend="{ value: '0', direction: 'flat' }"
        footnote="与昨日持平"
        hint="设备监控"
      />
    </NvSectionCards>
    <p class="text-sm text-muted-foreground">
      顶部为一级能力区（超出进入“更多”），左侧为当前域内菜单，右上为命令搜索（⌘K）、主题色与亮暗切换、用户菜单。
    </p>
  </BusinessLayout>
</template>
