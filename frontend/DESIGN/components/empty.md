# 空状态（Empty）

区块或页面区域没有数据可展示时使用的完整空状态。

> NvUI 状态：PC 层目前没有 `NvEmpty`；`Empty*` 系列是当前从
> `@nerv-iip/ui` 导出的规范名称（在品牌层重建前，原版 primitives 继续作为
> 应用侧名称）。移动端使用 `NvMobileEmpty`，它从 `@nerv-iip/ui-mobile` 导出。

## 何时使用哪种空状态

| 使用 `Empty`                                | 使用 `NvDataTable` 的 `emptyMessage` |
| ------------------------------------------- | ------------------------------------ |
| 没有数据的完整区块/页面（首次使用、零状态） | 表格查询返回 0 行                    |
| 筛选后非表格视图产生零结果                  | 在表格中筛选后                       |

（原版 `TableEmpty` 行属于手动组合的原版表格，应用代码已不再构建；
`NvDataTable` 会根据 `emptyMessage` 渲染自己的空行。）

## 用法

```vue
<!-- Full section empty state -->
<Empty>
  <EmptyMedia>
    <InboxIcon class="size-12 text-muted-foreground" aria-hidden="true" />
  </EmptyMedia>
  <EmptyHeader>
    <EmptyTitle>No instances found</EmptyTitle>
    <EmptyDescription>
      There are no application instances registered in this control plane yet.
    </EmptyDescription>
  </EmptyHeader>
  <EmptyContent>
    <NvButton type="button" @click="openCreate">Register instance</NvButton>
  </EmptyContent>
</Empty>

<!-- Inside a table: built into NvDataTable -->
<NvDataTable :rows="items" empty-message="No users match the current filters." … />
```

## 文案指引

- `EmptyTitle`：陈述式，例如“未找到用户”“没有活动会话”。
- `EmptyDescription`：说明原因和可采取的操作，例如“创建用户即可开始”。
- `EmptyContent`：可选 CTA 按钮，仅在存在明确的下一步操作时提供。

## 禁止

- 加载期间不得显示 Empty；应改为显示 Skeleton（或 `NvDataTable` 内置的 `loading`）。
- 不得使用笼统的“无数据”消息；应明确说明何处为空及其原因。
- 不得让零结果区域在视觉上留白；每个数据界面都需要明确的空状态。
