# 前端设计系统规划

第五阶段的后端基础工作曾暂停前端功能开发，直至能审慎选定控制台视觉系统。“Console Auth + shadcn-vue Baseline”（控制台认证与 shadcn-vue 基线）现记录了首个产品切片的这一选型。

## 已选基线

“Console Auth + shadcn-vue Baseline”选用 shadcn-vue 官方组件注册表、`reka-nova` 风格、Vite 模板、Reka 基础组件、Tailwind CSS v4 和语义 token 模型。组件源码位于 `frontend/packages/ui`，控制台应用只能通过稳定的 `@nerv-iip/ui` 导出使用它们。

此前本地的 `UiButton`、`UiPanel` 和 `UiBadge` 基础组件已迁移为 shadcn-vue 组件并删除，不再作为并行设计系统维护。

## 当前决策

1. 新的控制台 UI 工作必须使用已选定的 shadcn-vue 基线、语义 token 和 `@nerv-iip/ui` 导出边界。
2. 未经新的设计系统规格，不得引入第二个 UI 组件注册表、竞争性 token 系统、无关 CSS 框架或页面专用组件皮肤。
3. 不得将大型产品工作流作为附带后端工作加入；当工作流改变信息架构、导航、授权或视觉密度时，必须创建聚焦的前端/产品规格。
4. 当后端 OpenAPI 变更需要时，仍可进行 API client 生成和前端质量门禁。
5. Business Console 组件就绪度在 `frontend/DESIGN/roadmaps/business-console-readiness.md` 中跟踪；#143 在新增 shadcn-vue 基础组件或 FileUpload 封装组件前必须更新 DESIGN 组件契约。

## Phase 8 当前基线

Phase 8 将 IAM 管理控制台基线确立为蓝色 Calm Control Plane：克制的界面表面、蓝色主要动作、安静的中性结构和高密度的运营辅助元素。基线仍使用 Tailwind CSS v4、shadcn-vue `reka-nova`、Reka 基础组件、lucide 图标以及 `frontend/packages/ui` 中由源码维护的组件。

共享 UI 包现拥有 IAM 管理页面所需的 table、dialog、alert-dialog、checkbox、select、pagination 和 empty state 基础组件。控制台应用代码应将其视为产品基础设施，而非页面局部片段。

## Token 契约

控制台 CSS token 契约位于 `frontend/apps/console/src/assets/main.css`。Phase 8 将语义 shadcn token 固定为蓝色控制平面值，覆盖 `--primary`、`--ring`、`--accent`、侧栏活动状态和图表强调色，同时保留现有控制台页面使用的 legacy token 块。

Tailwind v4 的 `@theme inline` 仍是必需项，使 `bg-primary`、`text-muted-foreground`、`border-border` 和 `ring-ring` 等语义工具类从同一契约解析。变更 CSS 前，应先更新 `frontend/packages/ui/src/design-system.contract.test.ts` 中的 Vitest 契约。

## 组件治理

shadcn-vue 组件通过 CLI 管理，并在生成后接受审核。可为适配本工作区的包内导入路径调整生成文件，但团队不得手写组件注册表中组件的并行版本，或在控制台页面内分叉视觉变体。

`@nerv-iip/ui` barrel（集中导出入口）是控制台应用的公开边界。应用代码使用新的 shadcn 基础组件前，应先在此处导出，以便将组件注册表变动和导入路径变更限制在 UI 包内。

对于 FileUpload，视觉外壳属于 Nerv-IIP，应由 `@nerv-iip/ui` 组合而成。需要暂停/恢复、重试和 tus 兼容性时，首选使用带 `@uppy/tus` 的 Uppy core/headless 作为可续传上传引擎；Uppy Dashboard 的视觉皮肤不是设计基线。FileUpload 必须消费 FileStorage 的 upload-session 和 tus/download-grant 契约，且不得暴露 MinIO 对象 key 或直接对象存储 URL。

## IAM 管理模式

IAM 管理页面应由共享基础组件组合成高密度、任务聚焦的视图：用户、角色和权限使用 table；创建/编辑流程使用 dialog；破坏性确认使用 alert dialog；范围选择使用 select 和 checkbox；服务端列表使用 pagination；筛选结果为空或首次运行时使用 empty state。

控制台应用代码必须从 `@nerv-iip/ui` 导入这些控件，而不得从 `frontend/packages/ui/src/components` 或直接 shadcn 路径导入。页面专用样式只能使用语义 token 和布局类，使组件的颜色、排版、圆角和焦点行为仍由共享基线治理。

## 后续规格触发条件

变更下列任一决策前，必须另行创建 Superpowers 设计规格：

1. 超出 shadcn-vue 的组件库或组件注册表策略。
2. 色彩、排版、间距、层级、圆角或状态的设计 token 模型。
3. 运营密集型控制台页面的布局密度。
4. 主题策略、租户品牌，或对深色模式的产品承诺。
5. 超出当前键盘、焦点、对比度和响应式检查范围的可访问性基线。
6. 视觉回归测试策略。
