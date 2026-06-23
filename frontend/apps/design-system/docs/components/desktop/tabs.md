---
title: Tabs 标签页
---

<script setup>
import {
  TabsPro,
  TabsProList,
  TabsProTrigger,
  TabsProContent,
  Progress,
} from '@nerv-iip/ui'
</script>

# Tabs 标签页

在同一区域内切换平级视图。`TabsPro` 家族由 `TabsPro`（根）、`TabsProList`、`TabsProTrigger`、`TabsProContent` 组成，触发器与列表为复制重建样式，根与内容复用底层原语。

## 基础

<Demo>
  <div class="w-96">
    <TabsPro default-value="overview">
      <TabsProList>
        <TabsProTrigger value="overview">概览</TabsProTrigger>
        <TabsProTrigger value="quality">质量</TabsProTrigger>
        <TabsProTrigger value="maint">维护</TabsProTrigger>
      </TabsProList>
      <TabsProContent value="overview" class="pt-4">
        <p class="text-sm text-muted-foreground">当班共 12 条产线运行，2 条待料。</p>
        <div class="mt-4 space-y-2">
          <div class="flex items-center justify-between text-sm">
            <span>计划达成</span><span class="tabular-nums text-muted-foreground">68%</span>
          </div>
          <Progress :model-value="68" />
        </div>
      </TabsProContent>
      <TabsProContent value="quality" class="pt-4">
        <p class="text-sm text-muted-foreground">在线判定良率 99.2%，无重大不合格。</p>
      </TabsProContent>
      <TabsProContent value="maint" class="pt-4">
        <p class="text-sm text-muted-foreground">CNC-07 计划保养窗口：今晚 22:00。</p>
      </TabsProContent>
    </TabsPro>
  </div>
</Demo>

```vue
<TabsPro default-value="overview">
  <TabsProList>
    <TabsProTrigger value="overview">概览</TabsProTrigger>
    <TabsProTrigger value="quality">质量</TabsProTrigger>
    <TabsProTrigger value="maint">维护</TabsProTrigger>
  </TabsProList>
  <TabsProContent value="overview" class="pt-4">…</TabsProContent>
  <TabsProContent value="quality" class="pt-4">…</TabsProContent>
  <TabsProContent value="maint" class="pt-4">…</TabsProContent>
</TabsPro>
```

## 属性

| 组件 | 属性 | 说明 | 类型 |
|---|---|---|---|
| `TabsPro` | `default-value` | 默认激活的标签值（非受控） | `string` |
| `TabsPro` | `model-value` | 受控当前值（`v-model`） | `string` |
| `TabsProTrigger` | `value` | 该触发器对应的标签值 | `string` |
| `TabsProContent` | `value` | 该内容面板对应的标签值 | `string` |
