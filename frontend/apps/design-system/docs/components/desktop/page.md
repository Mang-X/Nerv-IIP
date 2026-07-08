---
title: NvPage 页面布局
---

<script setup>
import { NvPage, NvPageAside } from '@nerv-iip/ui'
</script>

# NvPage 页面布局

带可选左右栏的页面网格（参考 Nuxt UI）。大屏下为 10 栏网格——左栏 2 / 中间 6–8 / 右栏 2；不传侧栏时为居中单列。侧栏内容用 `NvPageAside` 包裹可获得吸顶独立滚动。

## 左导航 + 内容 + 右目录

<Demo>
  <NvPage class="rounded-xl border border-border bg-card p-4">
    <template #left>
      <NvPageAside>
        <nav class="space-y-1 text-sm">
          <a class="block rounded-md bg-accent px-2 py-1.5 font-medium text-brand-strong">概览</a>
          <a class="block rounded-md px-2 py-1.5 text-muted-foreground">工单 WO</a>
          <a class="block rounded-md px-2 py-1.5 text-muted-foreground">工序 OP</a>
          <a class="block rounded-md px-2 py-1.5 text-muted-foreground">设备 EQ</a>
        </nav>
      </NvPageAside>
    </template>
    <div class="min-h-40">
      <h3 class="text-lg font-semibold">主内容区</h3>
      <p class="mt-2 text-sm text-muted-foreground">中间列承载主内容。两侧栏存在时占 6 栏，单侧栏时占 8 栏，无侧栏时整页居中单列。侧栏在移动端隐藏（由抽屉等承载）。</p>
    </div>
    <template #right>
      <NvPageAside>
        <p class="mb-2 text-xs font-medium text-muted-foreground">本页目录</p>
        <ul class="space-y-1 text-sm text-muted-foreground">
          <li>基本信息</li>
          <li>工艺路线</li>
          <li>质检记录</li>
        </ul>
      </NvPageAside>
    </template>
  </NvPage>
</Demo>

```vue
<NvPage>
  <template #left>
    <NvPageAside><!-- 导航 --></NvPageAside>
  </template>

  <article><!-- 主内容 --></article>

  <template #right>
    <NvPageAside><!-- 本页目录 --></NvPageAside>
  </template>
</NvPage>
```

## 插槽

| 插槽      | 说明                                  |
| --------- | ------------------------------------- |
| `left`    | 左侧栏（导航），大屏 2 栏、移动端隐藏 |
| `default` | 主内容区                              |
| `right`   | 右侧栏（目录），大屏 2 栏、移动端隐藏 |

> `NvPageAside` 提供 `lg:sticky` 吸顶 + 独立滚动；放在 `#left` / `#right` 内即可。
