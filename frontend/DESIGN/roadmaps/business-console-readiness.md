# 业务控制台组件就绪度

本路线图是 #143 的规范性设计系统范围。Superpowers 计划可以引用它，但组件决策归属本文件。

## 决策

业务控制台就绪度首先是设计系统工作，其次才是实现工作。新增原语必须加入 `@nerv-iip/ui`、在 `frontend/DESIGN` 中记录，然后才能由应用页面使用。

## 即刻组件集

| 能力 | 设计系统方向 | #143 状态 |
| --- | --- | --- |
| 标签页（Tabs） | shadcn-vue `tabs` 样式与公共组成部分。 | 已在 `@nerv-iip/ui` 中交付；用于订单、工单、设备和 SKU 等密集详情页。 |
| 侧滑面板（Sheet） | shadcn-vue `sheet` 样式与公共组成部分。 | 已在 `@nerv-iip/ui` 中交付；用于从列表页滑入的检查、详情或编辑面板。 |
| 日期和日期范围选择器 | 基于 Popover 的紧凑型 DateOnly 控件。 | 已在 `@nerv-iip/ui` 中交付；当前界面使用原生日期输入，在加入样式化日历网格部件前，`Calendar`/`RangeCalendar` 仅作为底层 Reka 根组件导出。 |
| 图表 | 带语义 token 桥接的 shadcn 风格图表壳层。 | 已在 `@nerv-iip/ui` 中交付；页面级图表引擎仍是适配器，而非第二套设计系统。 |
| 文件上传 | 采用 shadcn 结构和 FileStorage 语义的 Nerv-IIP FileUpload 包装器。 | 已在 `@nerv-iip/ui` 中交付；界面通过 FileStorage 上传会话和 tus/server-proxy 传输，不得直接访问 MinIO。 |
| 进度 | shadcn-vue `progress` 样式。 | 已在 `@nerv-iip/ui` 中交付；供 FileUpload 和执行进度指示器使用。 |
| 滚动区域 | shadcn-vue `scroll-area` 样式。 | 已在 `@nerv-iip/ui` 中交付；避免页面局部滚动条样式。 |

## 文件上传（FileUpload）方向

#143 基线使用带有可插拔 `transport` 属性（prop）的轻量原生 FileStorage 传输实现。它支持当前 FileStorage `tus` `HEAD`/`PATCH` 路径和 `server-proxy` 二进制 `PUT` 指令，同时不向设计系统包引入 Uppy 的依赖负担。

当前包装器包括拖放、逐行进度、通过 `AbortController` 实现的暂停/恢复、传输尝试失败后的重试、常见 Office/PDF/媒体格式可读的文件类别标签，以及通过 Vue 过渡（transitions）和 Tailwind 语义类实现的轻量行/投放反馈动画。它通过 `autoUpload=false` 同时支持自动上传和手动队列模式，并提供刻意保持精简的公开组件 API，以编排表单级提交。大型队列会切换为固定高度的虚拟化滚动容器，避免批量附件工作流一次渲染每一行。

当上传需要更丰富的可恢复性控制、重试策略、暂停/恢复界面、来源提供方或更广泛的 tus 协议覆盖时，Uppy core/headless（核心/无界面模式）加 `@uppy/tus` 仍是首选适配器。它应位于相同的 Nerv-IIP 包装器契约之后。

不得将 Uppy Dashboard（仪表盘）用作默认视觉基线。其行为可以启发交互细节，但渲染壳层应保持 Calm Control Plane（沉静控制平面）并使用 `@nerv-iip/ui` 原语。

对于当前狭窄的 FileStorage 传输路径，在依赖负担比完整协议覆盖更重要时，可以使用自定义 tus 客户端。在扩展到大型 CAD 包或重型媒体工作流之前，它应保持可由 Uppy 支撑的适配器替换。

## 文件上传（FileUpload）契约

首个 FileUpload 原语应暴露：

1. `purpose`、`ownerService`、`ownerType`、`ownerId`、`organizationId`、`environmentId`。
2. 可接受的内容类型、最大文件大小和最大文件数。
3. 包含文件名、大小、匹配的文件类别、状态、进度和错误的当前上传行。
4. 仅已完成的 `fileId` 值；不得暴露对象键、存储桶（bucket）名称或长期有效的 URL。
5. 支持 FileStorage `server-proxy` 和 `tus` 模式的传输适配器。
6. 行级暂停/恢复、重试和移除操作。
7. 通过 `autoUpload=false` 提供手动队列模式，以及公开的 `addFiles`、`uploadQueued`、`pauseAll`、`resumeAll`、`retryFailed`、`clear` 和 `browse` 方法。
8. 被拒绝和失败的行应保持可见，且不得占用可用上传槽位。
9. 面向大型队列的虚拟化行渲染，阈值、行高和列表高度均可配置。
10. 针对大小/类型被拒绝、会话过期、校验和不匹配和上传中断的错误状态。

## 图表契约

图表应当：

1. 使用控制台 token 契约中的语义化图表 token。
2. 首批业务仪表板优先使用折线、柱状和圆环/饼图形态。
3. 在密集面板中保持图例、提示框和坐标轴可读。
4. 避免装饰性渐变或一次性调色板。
5. 使用现有 `Empty`、`Skeleton`、`Alert` 和 `Spinner` 原语提供空、加载和错误状态。

## 日期选择器契约

日期控件应当：

1. 为 MVP 表单和筛选器使用基于 Popover 的紧凑控件。
2. 为范围筛选支持清除、应用和取消行为。
3. 在 API 边界返回具有类型的、兼容 `DateOnly` 的 ISO 日期字符串。
4. 使用适合工具栏筛选和表单字段的紧凑触发器。
5. 避免页面局部日历样式；在样式化设计系统部件存在前，应用页面不得直接使用底层 `Calendar`/`RangeCalendar` 根组件。

## 侧滑面板和标签页契约

侧滑面板应当用于保留列表上下文的相邻详情或编辑界面。只有当详情对象具有多个同级区段时，才应使用标签页；它们不是主导航。

## 治理

1. 在 `frontend/` 下通过 CLI 安装 shadcn-vue 组件。
2. 从 `frontend/packages/ui/src/index.ts` 导出所有公共组成部分。
3. 在 `frontend/DESIGN/components/` 下新增组件规格说明。
4. 更新 `frontend/DESIGN/index.md`。
5. 导出边界或 token 契约变更时，新增或更新组件契约测试。

## 非目标

1. 不得将 MinIO/S3 多段上传（multipart）作为 #143 的一部分实现。
2. 不得创建第二套图表设计系统。
3. 不得暴露 FileStorage 对象键或直接对象存储 URL。
4. 不得在 `@nerv-iip/ui` 之外创建页面专用的上传、图表、日期、侧滑面板或标签页样式。
