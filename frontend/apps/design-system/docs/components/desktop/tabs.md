---
title: Tabs 标签页
---

<script setup>
import {
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
  NvTabsContent,
  Progress,
} from '@nerv-iip/ui'
</script>

# Tabs 标签页

在同一区域内切换平级视图。`NvTabs` 家族由 `NvTabs`（根）、`NvTabsList`、`NvTabsTrigger`、`NvTabsContent` 组成，触发器与列表为复制重建样式，根与内容复用底层原语。

## 基础

<Demo>
  <div class="w-96">
    <NvTabs default-value="overview">
      <NvTabsList>
        <NvTabsTrigger value="overview">概览</NvTabsTrigger>
        <NvTabsTrigger value="quality">质量</NvTabsTrigger>
        <NvTabsTrigger value="maint">维护</NvTabsTrigger>
      </NvTabsList>
      <NvTabsContent value="overview" class="pt-4">
        <p class="text-sm text-muted-foreground">当班共 12 条产线运行，2 条待料。</p>
        <div class="mt-4 space-y-2">
          <div class="flex items-center justify-between text-sm">
            <span>计划达成</span><span class="tabular-nums text-muted-foreground">68%</span>
          </div>
          <Progress :model-value="68" />
        </div>
      </NvTabsContent>
      <NvTabsContent value="quality" class="pt-4">
        <p class="text-sm text-muted-foreground">在线判定良率 99.2%，无重大不合格。</p>
      </NvTabsContent>
      <NvTabsContent value="maint" class="pt-4">
        <p class="text-sm text-muted-foreground">CNC-07 计划保养窗口：今晚 22:00。</p>
      </NvTabsContent>
    </NvTabs>
  </div>
</Demo>

```vue
<NvTabs default-value="overview">
  <NvTabsList>
    <NvTabsTrigger value="overview">概览</NvTabsTrigger>
    <NvTabsTrigger value="quality">质量</NvTabsTrigger>
    <NvTabsTrigger value="maint">维护</NvTabsTrigger>
  </NvTabsList>
  <NvTabsContent value="overview" class="pt-4">…</NvTabsContent>
  <NvTabsContent value="quality" class="pt-4">…</NvTabsContent>
  <NvTabsContent value="maint" class="pt-4">…</NvTabsContent>
</NvTabs>
```

## 属性

| 组件            | 属性            | 说明                       | 类型     |
| --------------- | --------------- | -------------------------- | -------- |
| `NvTabs`        | `default-value` | 默认激活的标签值（非受控） | `string` |
| `NvTabs`        | `model-value`   | 受控当前值（`v-model`）    | `string` |
| `NvTabsTrigger` | `value`         | 该触发器对应的标签值       | `string` |
| `NvTabsContent` | `value`         | 该内容面板对应的标签值     | `string` |
