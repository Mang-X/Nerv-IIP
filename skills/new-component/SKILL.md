---
name: new-component
description: 在 NvUI 组件库(@nerv-iip/ui / @nerv-iip/ui-mobile)新建或上提一个品牌组件的完整流程——判层、R1–R5 定名、实现约束、六件套 DoD 清单。当需要创建新组件、把 app 内业务组件上提进组件库、或复制重建原版件时使用。
---

# 新建 NvUI 组件

把"新建一个组件"从高风险动作变成标准动作。整个流程的权威依据：
ADR 0020（`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`）、
`frontend/DESIGN/governance.md`（选件阶梯 + 六件套 DoD）、
`frontend/packages/ui/AGENTS.md`（库内规则）。

## 0. 先确认真的该新建

按 `governance.md` 选件阶梯：现有 `Nv*` 件（查 `DESIGN/index.md` 速查表 +
`DESIGN/component-coverage.md` 矩阵）→ 组合现有件 → 新建。触发新建的信号：
交互表达不出来 / 凑合现有件要 2+ 个补丁 props / 在"和组件搏斗"。
**新建门槛低**——判断成立就动手，不要削足适履；但下面的规矩一条不能少。

## 1. 判层（表面决定一切）

| 目标表面 | 落点 | 命名前缀规则 |
|---|---|---|
| PC (console / business-console) | `packages/ui/src/components/pc/`（原子）或 `blocks/`（组合区块） | 素名优先 → `Nv` + 名 |
| 移动 PDA | `packages/ui-mobile/src/components/` | 撞名 → `NvMobile*`；移动专名 → `Nv*` |
| 一体机 touch | `packages/ui/src/components/touch/` | 撞名 → `NvTouch*` |
| 大屏 screen | `packages/ui/src/components/screen/` | 通用词 → `NvScreen*`；工业专名 → `Nv*` |

- 跨表面需求 = **建两件**（各表面一件），绝不"一件两模式"。
- 动手前**必读所在层的 product.md**：PC = `packages/ui/src/components/pc/product.md`，
  screen = `packages/ui/src/components/screen/product.md`。
- app 内上提（反哺）：把业务组件从 app 移入对应层时，去掉业务耦合
  （props 传数据，不 import app 的 store/composable）。

## 2. 定名（ADR 0020 §1.2 R1–R5 判定流程）

按序判定，第一条命中即停：R1 名字已含场景词根 → `Nv`+原名；R2 与原版/PC 素名
相同 → 加场景词根；R3 属于通用交互原语（Arco PC 名单为判据）→ 加场景词根；
R3b 复合名以通用词结尾且首词是专名 → `Nv`+原名；R4 工业专名/自造名 → 直接 `Nv`；
R5 `Status*` 家族特判。已冻结的逐件结果查 ADR 0020 附录 A，**不即兴起名**。

## 3. 实现约束

- **原版零改动**：shadcn 原版（`components/ui/`）byte-for-byte；定制 = 复制重建。
- **Token**：只用本场景命名空间 + 契约层 token；跨场景同值走 var 链
  （`--nv-scr-green: var(--nv-success)`），禁复制字面量；无裸 hex / 调色板类名。
- **样式进层**：SFC `<style>` 包 `@layer nv-components`；赢 utilities 的库级装饰
  进 `nv-overrides`（`styles/overrides.css`，该文件本身不分层）。
- **动效**：`--nv-ease-*` / `--nv-duration-*` 令牌（screen 层用 `--nv-scr-ease*`），
  press 收缩不回弹，全部 transform/keyframes 有 `prefers-reduced-motion` 降级。
  全清单：`DESIGN/motion-interaction.md` §7 自检。
- **交互状态完备**：default / hover / focus-visible / active / disabled / loading
  / selected，不做一半。
- **数据驱动**：零 props 可渲染合理示例；不写死业务文案。
- ui-mobile 不用 cva（无此依赖），用 computed 映射类名。

## 4. 六件套 DoD（缺一不算完成）

1. 源码落正确层，命名过 R1–R5；
2. barrel 导出（`packages/ui/src/index.ts` 或 ui-mobile `index.ts`）；
3. contract tests 全绿（`nvui-naming` / `ui-primitives` / 各 app `nvui-imports`）；
4. `frontend/DESIGN/component-coverage.md` 四场景矩阵加行；
5. `frontend/DESIGN/components/<name>.md` 决策段（使用时机 / 变体选择 / Do-Don't，
   四段式：规则/判定/正例/反例）；
6. design-system 文档站页（`apps/design-system/docs/components/<surface>/<name>.md`，
   live demo + `@include` 嵌 DESIGN 决策段）。

## 5. 门禁

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
# touched 文件逐一：
pnpm -C frontend exec vp fmt --check <file>
```

真机/预览走查按所在 app 的验收惯例（screen/PDA 有各自的 AGENTS.md）。
