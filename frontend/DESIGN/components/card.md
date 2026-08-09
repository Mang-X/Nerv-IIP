# Card (NvCard)

内容分组容器。用于设置分区、详情面板、统计摘要和登录表单。应用代码应使用
`NvCard*` 组件族，它从 `@nerv-iip/ui` 导入；无前缀的 `Card*` 部件是 shadcn 原版基础组件（primitive），
仅限组件库内部使用。对于 KPI 或统计磁贴，应优先使用专门构建的
`NvMetricCard`（仪表盘统计行可使用 `NvSectionCard`/`NvSectionCards`），而不是手动以
`NvCard` 拼装。

## 结构

```
NvCard
  NvCardHeader
    NvCardTitle
    NvCardDescription
    NvCardAction      (optional: right-aligned action button)
  NvCardContent
  NvCardFooter        (optional: form actions, links)
```

## 用法

```vue
<!-- Settings section or entity detail -->
<NvCard>
  <NvCardHeader>
    <NvCardTitle>User profile</NvCardTitle>
    <NvCardDescription>View and update basic identity information.</NvCardDescription>
  </NvCardHeader>
  <NvCardContent class="grid gap-4">
    <!-- content -->
  </NvCardContent>
</NvCard>

<!-- Card with header action -->
<NvCard>
  <NvCardHeader>
    <NvCardTitle>API keys</NvCardTitle>
    <NvCardAction>
      <NvButton variant="outline" size="sm" type="button">Add key</NvButton>
    </NvCardAction>
  </NvCardHeader>
  <NvCardContent>
    <!-- content -->
  </NvCardContent>
</NvCard>

<!-- Form card (e.g. login) -->
<NvCard class="w-full max-w-sm">
  <NvCardHeader>
    <NvCardTitle>Sign in</NvCardTitle>
  </NvCardHeader>
  <NvCardContent>
    <form class="grid gap-4" @submit.prevent="submit">
      <!-- fields -->
    </form>
  </NvCardContent>
  <NvCardFooter>
    <NvButton type="submit" class="w-full">Sign in</NvButton>
  </NvCardFooter>
</NvCard>
```

## 禁止

- 不得将 `NvDataTable` 包裹在 `NvCard` 内；该表格自带带边框的容器。
- 不得添加 `p-*` 内边距到 `NvCard` 本身；`NvCardContent` 已提供正确的内边距。
- 不得为每个分组都使用 `NvCard`；对于密集的管理页面，仅带标题的扁平分区更合适。
