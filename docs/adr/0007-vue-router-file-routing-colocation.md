# ADR 0007: Vue Router 文件路由与页面共置约定

- Status: Proposed
- Date: 2026-05-13

## Context

项目希望保留文件路由带来的低样板与 typed routes 能力，同时避免过强的框架魔法。团队明确不采用自造 middleware runtime，也不再使用 unplugin-vue-router 作为默认方案，而是转向 Vue Router 官方文件路由能力。

控制台页面会出现大量复杂列表、详情、弹窗、抽屉与局部片段；如果页面私有组件全部提升到全局 components，目录会迅速扁平化并产生命名膨胀。页面私有 .vue 组件若直接与页面入口并列，必须避免被扫描成真实路由。

## Decision

1. 文件路由统一采用 Vue Router 官方插件，通过 vue-router/vite 接入。
2. src/pages 是唯一真实页面来源，但必须保留显式的 src/router/index.ts 作为运行时入口。
3. 路由访问控制、布局切换、组织上下文校验统一通过 guards + route meta 实现，不引入独立 middleware 运行时概念。
4. 复杂页面默认采用文件夹加 index.vue 作为路由入口。
5. 页面私有 .vue 组件允许与页面目录共置，但必须放在约定的排除目录中，例如 components、dialogs、drawers、fragments。
6. 优先使用 exclude 规则排除页面私有 .vue 目录，不默认重写 filePatterns。
7. 页面目录下的 .ts 文件如 columns.ts、schema.ts、useXxx.ts 可直接共置，因为默认不会被扫描为路由。

## Rationale

1. 官方文件路由插件可以减少生态分叉，降低未来升级风险。
2. 保留 router/index.ts 可以让守卫、history、错误处理与 layout meta 保持显式控制点。
3. 文件夹加 index.vue 更适合复杂工业控制台页面，因为它天然支持页面入口与私有实现靠近放置。
4. exclude 方案比重写 filePatterns 更稳，能保留官方默认命名语义，降低团队心智负担。
5. 允许页面私有组件共置后，真正跨页面复用的组件会更自然地暴露出来，再决定是否上提。

## Consequences

1. 团队必须遵守页面入口文件与私有目录的命名约定，否则容易出现误扫路由或目录风格漂移。
2. 页面目录树会比传统单文件页面更深，需要通过约定维持可读性。
3. 如果页面私有组件被过早上提，会削弱本 ADR 的收益；如果长期不上提，也可能形成重复实现，需要在 code review 中把握边界。

## Implementation Notes

1. 控制台应用的 vite.config.ts 需要明确接入 vue-router/vite，并写入 exclude 规则。
2. src/router/index.ts 需要负责 createRouter、history、guards、scrollBehavior、错误处理与 route meta 解释。
3. 页面脚手架模板需要明确两种页面形式：简单单文件页面与复杂文件夹页面。
4. README 或前端规范文档中需要提供一组标准示例目录，帮助团队快速判断何时共置、何时上提。

## Out of Scope

1. 不在本 ADR 中定义每个具体业务页面的路由信息。
2. 不在本 ADR 中定义权限点枚举与具体 meta 字段值表。
3. 不在本 ADR 中决定是否引入额外的路由代码生成二次封装。
