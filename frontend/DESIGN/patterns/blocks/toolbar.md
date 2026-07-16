# Block: Toolbar（工具条）

> **现役实现 = `NvToolbar`**（`@nerv-iip/ui` blocks 层，
> `packages/ui/src/components/blocks/toolbar/Toolbar.vue`）。本文早期版本描述的
> `IamListToolbar`（自带 actionLabel/statusOptions 的封闭组件）**已不存在**——被开放
> 槽位的 `NvToolbar` 取代；旧接口存档于文末附录。

## 规则

1. **单行**：搜索 + 筛选 + 动作一行放完（移动端自动竖排，`sm:` 起横排）；不做多行筛选面板。
2. **搜索走内建输入**：`v-model:search`（live，防抖归页面）；`searchLabel` /
   `searchPlaceholder` 写清搜索口径（如「搜索任务、工单、设备」）。不需要搜索时
   `:show-search="false"`。
3. **筛选进 `#filters` 槽**：状态/范围等用 `NvSelect` 族；筛选值（防抖后）双向同步 URL
   query（`interaction-patterns.md` §5.3）。
4. **动作进 `#actions` 槽**（自动靠右）：至多一个主动作（［+ 新建 X］），可配次操作
   （重置/导出）；权限不足时 `disabled`，不隐藏。
5. **工具条不含过滤逻辑**：它只派发模型值，过滤/请求归页面（或 composable）。
6. 工具条与表格是兄弟节点（页面 `gap` 分隔），不放进表格组件内。注意 `NvDataTable`
   自带内建工具条（`searchable`/`columnSettings`）——同页二选一：用页面级 `NvToolbar`
   时把表格内建搜索关掉（`:searchable="false"`），避免双搜索框。

## 判定

- 「搜索/筛选/主动作是不是同一行、且由 `NvToolbar` 承载？」自拼一行 flex → 打回换区块。
- 「同屏是否出现两个搜索框（`NvToolbar` + `NvDataTable` 内建）？」是 → 关掉其一。
- 「筛选逻辑写在工具条里了吗？」是 → 上移到页面。

## 正例

`apps/business-console/src/pages/mes/operation-tasks.vue:319`（黄金标准页）：

```vue
<NvToolbar v-model:search="keyword" search-placeholder="搜索任务、工单、设备">
  <template #filters>…状态/工作中心/班次 NvSelect…</template>
  <template #actions>…重置…</template>
</NvToolbar>
```

`apps/console/src/pages/iam/users/index.vue:265`（`:search` + `@update:search` 写法，
`#filters` 放状态 Select）。

组件接口（`Toolbar.vue` 源码为准）：props `search`、`showSearch`（默认 true）、
`searchPlaceholder`、`searchLabel`；emit `update:search`；slots `#filters`、`#actions`。

<!-- 反例：暂无现网证据；2026-07-11 实机走查未发现工具条形态违例（问题集中在筛选不进 URL，归 interaction-patterns §5.3）。 -->

---

## 附录：`IamListToolbar` 旧接口（组件已删除）

旧组件把主动作与状态筛选做成 props（`actionLabel`/`actionDisabled`/`showStatusFilter`/
`statusOptions` + `v-model:search`/`v-model:status`，emit `action`）。可保留决策
（升入现规范）：搜索占满剩余空间、动作恒右置、≤1 主动作、权限用 disabled 门控、
「工具条只派发模型值」。封闭 props 形态被 `#filters`/`#actions` 开放槽取代。
