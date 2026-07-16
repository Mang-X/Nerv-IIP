# skills/ — 项目专属 Agent Skills（源目录）

本目录是 Nerv-IIP 项目专属技能的**单一事实源**，随仓库版本化、随 PR 评审演进。
每个技能一个子目录，入口为 `SKILL.md`（frontmatter `name` + `description` +
正文指令），格式遵循 Agent Skills 约定，可被 Claude Code / Codex 等安装消费。

## 安装到本机 agent

```bash
# 从仓库本地路径安装（--copy 复制为快照；技能更新后重跑同一命令覆盖）
npx skills add ./skills/new-component --copy
```

> 注意：`skills update` 对 `--copy` 安装不可用，更新一律重跑 `add --copy`
> （与 doc-steward 技能仓库同一惯例）。

## 现有技能

| 技能 | 用途 |
|---|---|
| `new-component` | 在 NvUI 组件库新建/上提品牌组件的完整流程（判层、R1–R5 定名、实现约束、六件套 DoD） |

## 新增技能的规矩

1. 技能内容只写**本仓库特有**的流程与约定，通用知识不进技能；
2. 引用仓库文件用相对路径（`frontend/DESIGN/...`），技能在仓库根目录上下文执行；
3. 技能引用的规范文件（ADR / DESIGN / AGENTS.md）变更时，同 PR 检查技能是否需要同步。
