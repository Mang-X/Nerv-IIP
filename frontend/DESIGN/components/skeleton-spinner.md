# 骨架屏 / NvSpinner / NvLoader

加载指示器。Skeleton 映射内容形状；NvSpinner/NvLoader 表示正在进行的工作。

> NvUI 状态：`Skeleton` 保留原版；控制台行内加载使用 PC 品牌件 `NvSpinner`，默认读屏名称为「加载中」。原版 `Spinner` 继续导出且保持零改动。品牌组件 `NvLoader`（变体 `ring | dots | bars | pulse`）用于更丰富的品牌色行内加载。`NvButton` 内置 `loading` prop，因此按钮不得手动组合 spinner。

## Skeleton

用于初始数据加载，在数据到达前替换内容区域。
注意：`NvDataTable` 通过 `loading` + `skeletonRows` 渲染自己的骨架行；
不得手动重建表格骨架屏。

```vue
<!-- Card content skeleton -->
<div class="grid gap-3 p-6">
  <Skeleton class="h-6 w-48" />
  <Skeleton class="h-4 w-full" />
  <Skeleton class="h-4 w-3/4" />
</div>
```

## NvSpinner / NvLoader

用于行内加载：后台刷新、小型异步指示器。

```vue
<!-- 按钮提交：使用 NvButton 内置加载 -->
<NvButton type="submit" :loading="pending">登录</NvButton>

<!-- 页面行内刷新指示器 -->
<div class="flex items-center gap-2 text-sm text-muted-foreground">
  <NvSpinner class="size-3" aria-hidden="true" />
  正在刷新…
</div>

<!-- 品牌色加载器 -->
<NvLoader variant="ring" size="sm" />
```

## 决策指引

| 情形                       | 使用方式                                  |
| -------------------------- | ----------------------------------------- |
| 页面或区块初始加载         | Skeleton                                  |
| 表格初始加载               | `NvDataTable :loading`（内置骨架行）      |
| 按钮操作进行中             | `NvButton :loading`                       |
| 后台重新获取（数据已可见） | 与旧数据同时显示 `NvSpinner` / `NvLoader` |
| 整页空白加载               | 与页面布局匹配的 Skeleton 网格            |

## 禁止事项

- 初始页面加载不得使用 `NvSpinner`，应使用 Skeleton。
- 不得在按钮内手动组合 spinner，应使用 `NvButton :loading`。
- 少于约 200ms 后不得显示加载状态（考虑 `suspense` 延迟）。
- 不得为 Skeleton 使用随机宽度，应匹配预期内容宽度。
