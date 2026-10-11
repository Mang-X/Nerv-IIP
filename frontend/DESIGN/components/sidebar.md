# 导航侧栏（NvSidebar）

## 使用时机

PC 应用壳使用 `NvSidebar`，组合原版 `SidebarProvider`、`SidebarContent`、菜单与页脚部件。移动视口沿用上下文的 `openMobile` 状态，抽屉提供中文标题、描述与关闭按钮。

## 变体选择

`side`、`variant` 与 `collapsible` 沿用原版侧栏语义。应用壳通常采用 `collapsible="icon"`；始终展开的嵌入导航使用 `collapsible="none"`。

## 正例

通过 `@nerv-iip/ui` 导入 `NvSidebar`，由同一个 `SidebarProvider` 管理开合，使用 `NvSidebarTrigger` 和 `NvSidebarRail` 作为开合入口。

## 反例

不要修改原版 `Sidebar` / `SheetContent` 的读屏文案，也不要在应用中另建侧栏状态或抽屉。
