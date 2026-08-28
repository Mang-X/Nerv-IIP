# 前端设计系统治理

本文规定 Nerv-IIP Console / Business Console 等桌面 Web 前端的设计系统、token、共享组件和样式隔离边界。历史阶段、迁移批次和“已完成多少组件”不在本页维护；当前实现以 `frontend/` 代码、workspace 配置和组件导出为准。

## 基线边界

1. UI 基线采用当前仓库已选的 Vue/shadcn-vue 体系，并以 `frontend/packages/ui` 的 `@nerv-iip/ui` 公共导出作为跨应用组件边界。
2. 不在业务应用中并行维护第二套 Button/Input/Dialog/Table 等基础组件体系；确有不同交互语义时先判断应扩展共享组件、组合现有原语还是新增产品组件。
3. 不引入第二套完整 design-token registry、CSS framework 或 primitive system，除非有独立 spec/ADR 说明其无法由当前系统覆盖的真实问题与迁移边界。
4. 页面业务逻辑不得依赖 shadcn-vue 生成目录的内部文件布局；跨 package 使用稳定 barrel/public export。

## Token 规则

1. 页面和共享组件优先使用语义 token，不复制品牌色/边框色/阴影/圆角的裸值形成局部主题。
2. Console 的主题变量与 Tailwind 映射以当前 CSS producer 为准；需要精确 token 时直接读取 `frontend/apps/console/src/assets/main.css` 及共享样式配置，不在本文复制易漂移色值。
3. `@theme inline` 或等价映射只负责把设计语义暴露给工具链，不建立第二套与 CSS variable 平行的 token source。
4. 新 token 必须表达可复用语义，而不是某一页面坐标、某一临时截图颜色或单个组件的私有 hack。
5. 状态色、危险动作、成功/警告/错误语义必须与可访问性和交互状态一起评审，不能只改视觉颜色。

## 组件生成与维护

1. shadcn-vue/生成器只负责引入可维护源码，不等于组件已经符合 Nerv-IIP 设计规则；生成后必须评审 token、尺寸、keyboard/focus、loading/disabled/error 等状态。
2. 新增共享组件前检查现有 `@nerv-iip/ui` exports，避免同义组件和重复封装。
3. 共享组件只承载跨应用稳定交互/视觉语义，不读取具体业务 store、route、permission 或服务 DTO。
4. 业务领域组件留在对应 app/domain；当相同交互在多个应用真实复用后，再抽到共享层。
5. 删除/替换旧组件时先迁完调用方，再移除旧 export；不要长期保留双入口让新旧 API 并存。

## 导入边界

- 应用从 `@nerv-iip/ui` 等公共入口导入共享组件；不得跨 package 深链 `src/components/...`。
- `frontend/packages/ui` 可以依赖其声明的 primitive/tooling，但不得反向依赖具体 Console/Business Console。
- 应用特有页面壳、数据装配、权限判断和路由不进入通用 UI package。
- API client、auth、business-core 等逻辑 package 与 UI package 保持职责分离；UI 不成为新的业务 service locator。

## 高密度管理界面

1. 管理后台优先清晰的信息层级、稳定栅格、可扫读表格和可预测操作位置，不用装饰性效果替代信息架构。
2. 表格、筛选、详情/编辑面板和 destructive action 必须有明确 loading/empty/error/disabled/permission-denied 状态。
3. 高风险操作使用清楚的文案、确认与结果反馈；不能仅靠颜色区分。
4. 长表单按业务分组和依赖关系组织，不为了组件展示把一个工作流拆成无意义步骤。
5. 页面级 IA/业务流程变化属于 Product/feature 任务，不以“设计系统统一”为名越权重做业务流程。

## FileUpload 等复杂组件

复杂共享组件必须明确：

- 受控值与事件契约；
- progress / retry / cancel / error 状态；
- 文件大小、类型、数量等限制来自调用方/后端契约，而不是 UI 自行发明；
- 可访问的 keyboard/focus/label 行为；
- 不在组件内绑定某一个服务端上传 provider 或业务对象语义。

同理，DataTable、Combobox、Dialog、Drawer 等公共组件只承载通用交互能力，业务过滤器/权限/字段语义由调用方提供。

## API 与生成客户端

设计系统调整不得阻塞正常 OpenAPI/api-client 生成流程。公开 API 变化仍按当前 API/codegen Governance/Architecture 更新客户端；不要在 UI wrapper 中手写第二份 DTO 来“稳定”生成契约。

## 变更触发条件

以下情况应单独评估设计系统决策，而不是在页面 PR 中顺手改变全局规则：

1. 需要引入第二套 primitive/component framework；
2. 需要改变 token namespace 或全局主题模型；
3. 需要打破 `@nerv-iip/ui` 公共导入边界；
4. 共享组件 API 的破坏性变化影响多个应用；
5. 新交互模式会改变产品级 IA、导航或关键业务工作流。

长期取舍使用 ADR；当前实现调整按聚焦 Issue/PR 完成。历史 Phase、旧组件迁移和 roadmap 票号通过 Git/Reports/Tracker 追溯，不进入本页。