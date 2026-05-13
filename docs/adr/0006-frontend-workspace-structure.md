# ADR 0006: 前端工作区与目录结构

- Status: Proposed
- Date: 2026-05-13

## Context

Nerv-IIP 前端既要保留工程边界，又要适合 AI 协作与持续代码生成。项目将同时包含控制台应用、接口生成包、通用 UI 包与平台层能力包，单一 src 无法稳定承载这些职责。

团队接受 Nuxt 风格的目录语义，但不接受 Nuxt 式运行时约束，也不希望引入过强的隐式魔法。Vite+ 已覆盖格式化、检查与任务编排的默认需求，前端不再默认叠加 ESLint、Prettier、Turborepo。根级工作区检查配置与应用级 Vue 运行时配置需要分离，否则职责会持续耦合。

## Decision

1. 前端采用 pnpm workspace 管理 apps 与 packages。
2. 根级 vite.config.ts 只负责 Vite+ 工作区行为，包括 check、fmt、run 与共享检查规则。
3. 每个应用维护自己的 vite.config.ts，负责 Vue、vue-router/vite、alias 与 build 选项等运行时配置。
4. 控制台应用采用显式 Vue 目录结构：main.ts、App.vue、router、layouts、pages、components、composables、stores、api、plugins、utils。
5. 默认不创建 src/app；仅当 bootstrap 拆分复杂度明显升高时再引入可选的 src/app。
6. 跨应用或跨领域复用能力放入 frontend/packages，首批固定 ui、app-shell、api-client、layer-base、layer-platform 这些共享边界。
7. 生成代码与手写代码必须隔离，尤其是 api-client/generated 与应用侧 src/api 之间不得混放职责。

## Rationale

1. 显式入口与稳定目录比抽象完整更适合当前阶段，也更利于 AI 正确定位修改位置。
2. 根级配置与应用级配置分层后，工作区规则不会污染单应用运行时配置，后续扩展第二个前端应用时也无需重构根配置。
3. 共享包边界先行，可以避免控制台应用在早期演化成新的大一统代码仓。
4. 不强制 src/app 可以降低样板复杂度，同时保留在入口层变重时再抽离的空间。
5. 用 packages/api-client 承载契约生成产物，可以把接口变化的影响限定在明确的消费边界内。

## Consequences

1. 前端仓库会比单应用模板多出 apps 与 packages 分层，初始化成本略高。
2. 团队需要明确哪些能力属于应用内共享，哪些必须上提到 package，否则边界会重新模糊。
3. 根级脚本与应用级脚本会同时存在，README 与脚手架必须明确入口命令。
4. 如果将来只保留一个前端应用，这个结构会有轻微超前设计成本，但仍在可接受范围内。

## Implementation Notes

1. ADR 需要明确仓库根、frontend/apps/console、frontend/packages/api-client、frontend/packages/ui 的职责边界。
2. 首批脚手架需同时落下根级 package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json，以及 console 应用自己的 vite.config.ts。
3. README、架构图与脚手架目录树必须与本 ADR 保持一致。

## Out of Scope

1. 不在本 ADR 中决定视觉设计系统细节。
2. 不在本 ADR 中决定具体 UI 组件库实现方案。
3. 不在本 ADR 中决定第二个前端应用是否立即创建。
