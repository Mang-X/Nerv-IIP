# skills/ — 项目专属 Agent Skills（源目录）

本目录是 Nerv-IIP **项目专属**技能的单一事实源，受 git 跟踪、随 PR 评审演进。
每个技能一个子目录，入口为 `SKILL.md`。

## 先判断该放哪一层

技能有三层，装法和归属规则各不相同。新增技能前先定层，放错层的后果是它对某些
harness 不可见、或永远不会更新。

| 层 | 位置 | 装法 | 放什么 |
|---|---|---|---|
| **项目专属** | `skills/`（**受 git 跟踪**） | 手工 `npx skills add ./skills/<name> --copy` | 只有本仓库才成立的流程 |
| 第三方技能 | `.agents/skills/`（gitignored） | [`skills-lock.json`](../skills-lock.json) → `npx skills experimental_install` | 外部来源技能，带 hash 锁 |
| 跨 harness 通用 | `~/.agents/skills/` + `~/.agents/.skill-lock.json` | `npx skills add -g` | 与本仓库无关的通用技能 |

判据：**这条流程换到另一个仓库还成立吗？** 成立就不属于 `skills/`——通用技能放全局层，
放这里会把项目仓库变成通用技能的仓库。

`skills update` 对 `--copy` 安装不可用：项目专属技能改动后，更新一律重跑
`npx skills add ./skills/<name> --copy` 覆盖。

`~/.claude/skills/` 不是可选层：它只有 Claude Code 能读，`skills` CLI 也管不了它，
不要往那里安装。

## 再判一次：该是技能，还是该进 AGENTS.md

本仓库按路径自动加载从仓库根到目标路径的全部 `AGENTS.md`（见根 [`AGENTS.md`](../AGENTS.md)）。
因此「改某个目录的代码要遵守什么」写进那个目录的 `AGENTS.md` **严格更好**：投递是自动的，
不需要安装、也不依赖 `description` 被命中。技能的优势是**按语义触发的跨路径流程**——
PR 评审、走查取证、发布收尾这类没有固定目录可挂靠的工作。

判据：**按目录挂得住 → 进 AGENTS.md；跨路径、由任务语义触发 → 才是技能。**
这条比上面的「换个仓库还成立吗」更常用，因为多数候选技能失败在这一关。

## 写作规范

**REQUIRED BACKGROUND：** 通用技能写法由 `writing-skills` 技能拥有（来源
`obra/superpowers`，经 `skills-lock.json` 装入 `.agents/skills/`，该目录 gitignored、
需先跑一次安装），包括 TDD 式的基线测试、SDO 发现性优化、何时该用流程图、token 预算。
本节只写**本仓库追加**的约定，不复述它。

1. **`description` 只写触发条件，绝不概述工作流。** 这是 `writing-skills` 的实测结论：
   描述里概述了流程，代理会照描述执行而跳过正文——一份描述写"任务之间做代码评审"，
   导致代理只做了一次评审，而技能正文明确要求两次。写「当需要 X 时使用」，
   不写「本技能先 A 再 B 最后 C」。

2. **开头声明这是引导还是清单。** 需要执行者保留判断的技能，第一段写明
   「本技能是引导，不是清单」，并说清哪些判断留给执行者。否则代理会机械照单执行。

3. **权威来源段：链接，不复述。** 规则的唯一住所是 ADR / DESIGN / AGENTS.md，
   技能只负责路由过去。设一段「权威依据（读，不要复述）」列出链接。
   复述会产生第二个住所，规范一改技能就悄悄过期——这也是本规范
   不再要求「规范变更时同 PR 检查技能是否同步」的原因：不复述就没有同步成本。

4. **与其它技能重叠时写明所有权边界。** 例：「本技能拥有组件定名与实现约束；
   门禁命令归 `frontend-gate`；设计取向归 `frontend-design`。」

5. **昂贵或只应人工触发的流程加 `disable-model-invocation: true`。** 真机走查、
   起栈彩排、PDA 实机这类动辄几十分钟的流程，不加就会被自动触发烧掉时间。
   需要用户点名调用时配合 `argument-hint`。该字段在已安装的技能里广泛使用，
   可用 `grep -rl 'disable-model-invocation' .agents/skills/` 查当前实例。

6. **多 harness 适配器放 `agents/`。** 让 codex 等消费同一份技能源。`openai.yaml`
   当前的字段是 `display_name` / `short_description` / `default_prompt`，但**形状由消费方
   CLI 决定、且已知存在包一层 `interface:` 的变体**——落地前照抄一个已安装实例，
   不要凭记忆写：`cat .agents/skills/documentation/agents/openai.yaml`。

7. **引用仓库文件用相对路径**（`frontend/DESIGN/...`）；技能在仓库根目录上下文执行。

8. **不要用 `@file` 语法引用其它技能**——它会立即强制加载并烧掉上下文。
   用技能名加显式标记（`REQUIRED BACKGROUND:` / `REQUIRED SUB-SKILL:`）。

## 落地前检查

- [ ] 定了层：确认这条流程换个仓库不成立，才放 `skills/`
- [ ] 确认它按目录挂不住（否则应写进那个目录的 `AGENTS.md`，投递自动、无需安装）
- [ ] `description` 只有触发条件，无工作流概述
- [ ] 权威来源段只有链接，正文没有复述 ADR / DESIGN 的规则原文
- [ ] 与相邻技能的所有权边界已写明
- [ ] 昂贵流程已加 `disable-model-invocation: true`
- [ ] 按 `writing-skills` 的要求做过基线验证（无技能时代理怎么做，有技能后是否照做）
- [ ] 安装后在本会话实际触发过一次，确认它真的可被发现和加载

## 现有技能

当前无项目专属技能。

`new-component` 于 2026-08-18 删除，理由记在此处以免重演：内容约七成复述
`frontend/packages/ui/AGENTS.md`（该文件按路径自动加载，投递机制严格更好）、
ADR 0020 §1.2 与 `DESIGN/governance.md`；六件套 DoD 已由 `nvui-doc-coverage` 等契约测试
强制（baseline shrink-only），属于上文「能校验就自动化」的范围；而且它从未接入任何安装
通道，对 agent 从未可见，因此也没有「删掉会损失什么」的证据。

## 已知欠账

**`skills/*` 未接入 worktree 自动安装通道。** `scripts/setup-worktree.ps1` 会按
`skills-lock.json` 为新 worktree 装齐第三方技能，但不处理本目录——在这里新增技能后，
除非有人手工跑 `npx skills add`，它对任何 agent 都不可见。前身 `new-component` 就是这样
从未生效过。两条候选解法均未验证可行性：把 `skills/*` 登记进 `skills-lock.json`
（当前所有条目都是 `sourceType: github`，本地路径是否受支持待验），或让
`setup-worktree.ps1` 遍历 `skills/*` 逐个 `add --copy`。

**新增第一个技能前必须先解决这条**，否则会重演一次"写了但从未生效"。
