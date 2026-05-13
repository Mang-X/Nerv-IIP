# 前端结构与命名规范

本文档定义 Nerv-IIP 前端工作区的目录职责、配置分层、路由规则、页面共置策略、状态管理边界和命名习惯。

## 总体原则

1. 目录职责借鉴 Nuxt 的清晰语义，但运行时坚持 Vue 原生范式。
2. 默认入口采用 src/main.ts + src/App.vue，不默认创建 src/app。
3. 文件路由与显式 router/index.ts 同时存在。
4. 页面保持薄，视图拼装与局部交互逻辑优先放入 composables 和 query/mutation 层。
5. 生成代码与手写代码目录必须隔离。

## 工作区结构

```text
frontend/
  apps/
    console/
      src/
  packages/
    ui/
    app-shell/
    auth/
    shared-types/
    api-client/
    layer-base/
    layer-platform/
```

## 配置分层

### 根级配置

- package.json：工作区脚本入口与前端依赖入口。
- pnpm-workspace.yaml：纳入 apps 与 packages。
- vite.config.ts：工作区级 Vite+ 配置，只负责 check、fmt、run、workspace task 定义。
- tsconfig.base.json：前端共享 TypeScript 基线。

### 应用级配置

- frontend/apps/console/package.json：控制台应用脚本。
- frontend/apps/console/vite.config.ts：Vue、Vue Router 官方文件路由插件、alias 和构建配置。
- frontend/apps/console/tsconfig.json：纳入 typed routes 相关类型。

## 控制台应用目录职责

### 基础入口

- src/main.ts：应用启动、Provider 安装、router 与 store 装配。
- src/App.vue：根壳层，不承载业务逻辑。
- src/app：可选 bootstrap 目录，仅在入口拆分明显增多时引入。

### 运行时骨架

- src/router：router/index.ts、guards、meta、route helpers。
- src/layouts：布局组件。
- src/pages：真实页面入口。
- src/components：共享与局部视图组件。
- src/composables：跨组件与跨页面复用逻辑。
- src/stores：Pinia client state。
- src/api：应用侧 API 组装层。
- src/plugins：应用级插件安装。
- src/utils：纯函数工具。

## 路由规范

1. 文件路由统一采用 Vue Router 官方文件路由插件。
2. src/pages 是唯一页面来源，但必须保留显式的 src/router/index.ts。
3. route meta 描述访问控制、布局、feature flag 和页面标题。
4. guards 统一放在 src/router/guards。
5. 不引入单独 middleware runtime。

### 页面命名建议

- index.vue
- users/[id]/index.vue
- settings/audit/index.vue
- apps/[appId]/index.vue

## 页面共置规则

### 简单页面

- 直接使用单文件页面，例如 apps/index.vue、audit/index.vue。

### 复杂页面

- 采用文件夹加 index.vue 模式。
- 页面私有视图组件放 components。
- 页面私有弹窗放 dialogs。
- 页面私有抽屉放 drawers。
- 页面私有片段放 fragments。
- 页面专属 columns.ts、schema.ts、useXxx.ts 可以直接共置。

### 排除策略

1. 优先使用 exclude 排除页面私有 .vue 组件目录。
2. 默认不重写 filePatterns。
3. 共置的 .ts 文件默认不会被扫描为路由。

### 上提规则

1. 只被一个页面使用的组件，留在页面目录。
2. 被同一领域多个页面复用的组件，上提到 src/components/领域名。
3. 被多个应用或多个领域复用的组件，上提到 packages/ui 或 layer 包。

## 状态与请求分层

### Pinia

- 只管理客户端状态。
- 保存用户会话、组织上下文、环境上下文、布局偏好、命令面板状态。

### Pinia Colada

- 只管理服务端状态。
- 列表、详情、查询缓存、失效、重试和 mutation 生命周期统一走 Colada。

### api-client

- frontend/packages/api-client 负责生成 types、sdk、client 以及 Colada 查询和变更函数。
- 应用层只从稳定导出入口消费，不直接引用 generated 深层路径。

## 命名规则

### 组件

- UiXxx：纯 UI 组件。
- AppXxx：应用壳级组件。
- TheXxx：全局结构组件。
- IamXxx、OpsXxx、HubXxx、KnowledgeXxx：领域组件。

### 逻辑

- useXxx：composable。
- authStore、layoutStore：Pinia store。
- auth.ts、permission.ts、env-context.ts：router guards。

### 插件

- 使用有序前缀，例如 10.auth.ts、20.query.ts、30.icons.ts。

## 首批脚手架交付物

1. 根级 package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json。
2. console 应用的 main.ts、App.vue、vite.config.ts、router/index.ts、guards、layouts/default.vue、pages/index.vue。
3. packages/ui、packages/app-shell、packages/api-client、packages/layer-base、packages/layer-platform 初版。
