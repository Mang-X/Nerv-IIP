---
name: nerv-pr-review
description: 审 Nerv-IIP 的 PR、复审改动后的 head，或要派并行评审席位时使用。
---

# 审 Nerv-IIP 的 PR

**本技能是引导不是勾选表。** 一条带证据的阻断项，胜过一串挑剔。

审核分三**轴**并行，各派一个**席位**（子代理），结论分列：

| 轴 | 问什么 |
|---|---|
| 标准 | 改动违反本仓库已文档化的规则吗 |
| 规格 | 改动做的是 issue / spec 要的那件事吗 |
| 证据 | 改动声称的东西，证据成立吗 |

三轴正交，一轴过另一轴照样能挂：代码全合规却实现了错的东西；照 issue 做对了却破坏既有约定；两者都对但断言在错误实现上依然绿。分列汇报就是为了让一轴掩盖不了另一轴。

## 1. 钉住事实源

派席位**之前**做完。失败要暴露在这一步——ref 解析不出、diff 为空、改动面算不出来，就停在这里，别让三个席位各自跑一遍错的基线。

```bash
gh pr view <N> --json headRefOid,baseRefName,mergeable,files
git fetch origin "pull/<N>/head:pr-<N>"
git rev-parse "origin/<baseRefName>"    # baseRefName 是分支名；-BaseSha 只收 40 位全长 SHA
pwsh -NoProfile -File scripts/get-ci-impact-plan.ps1 -BaseSha <base> -HeadSha <head>
```

仓库里的 `.ps1` 没有执行位，`./scripts/...` 在 bash 下会 `permission denied`；CI 用 `shell: pwsh` 才不需要这层包装。

- 核「某处是否存在」一律用 **PR head 树**。本地工作树不作数：150+ 工作树共享一个 `.git`，并行会话会把 HEAD 切走。
- head SHA 双源交叉：`gh pr view` 与 `git ls-remote` 各取一次，对上再往下走。
- retarget、force-push、合并 main 之后：本步重做。上一轮的行内锚点和结论随 history rewrite 一起失效。

完成判据：head SHA、base SHA、改动路径清单三样都拿到且非空。

**这一步的产出本身就是发现来源。** 改动面里出现 `unclassified-path`、`rule-self-check` 之类的信号，是还没派席位就已经到手的阻断项——比席位早，也便宜得多。

## 2. 派三个席位

每席位一份**隔离副本**——`git archive` 出 head 树到独立目录。席位会跑变异测试和构建，共享一棵树会互相清掉产物。

```bash
for axis in standards spec evidence; do
  D=/tmp/rv-<N>-$axis
  mkdir -p $D && git archive pr-<N> | tar -x -C $D
  git diff $(git merge-base origin/<base> pr-<N>) pr-<N> > $D/REVIEW-<N>.patch
done
```

`git archive` 的产出**不含 `.git`**，席位在副本里跑不了任何 git 命令。diff 必须一并投进去，并在提示词里写明这一点，否则席位会去共享工作树上找。

派单时：

- **判据整段粘进提示词。** 席位读不到本技能文件，也读不到你的会话。
- 中间文件名带 PR 号——并行席位共享 scratchpad，同名文件会串台。
- 席位自己不再派子代理。
- 每轴 400 字以内，阻断项与建议分开写。

### 标准轴

判据来源：从仓库根到每个改动路径的全部 `AGENTS.md` 与 `AGENTS.override.md`，加 `docs/architecture/` 下相关治理文档与 ADR。

报告改动违反了哪条已文档化的规则，每条给出规则所在的文件与行。区分硬违反（文档写了「必须」「不得」）与判断题。

**只报绿门禁拦不住的问题。** 门禁会红的事情不需要人看。

### 规格轴

判据来源：`gh issue view <票号>`（票号取自 PR body 的 `Fixes #` / `Closes #`），以及 `docs/superpowers/specs/` 下对应规格。

报告三类：spec 要求了但缺失或只做一半；spec 没要求却做了（范围外）；看着实现了但实现错。每条引 spec 原文。

PR 没有关联票时按序找，找完仍无就明说「无规格可依」——实现本身不能当需求用：

1. PR body 自己声明的交付范围
2. base 树里被本 PR 声称关闭的欠账段或 TODO（`gh api repos/<owner>/<repo>/contents/<path>?ref=<base>`）
3. `docs/superpowers/specs/` 下同主题规格

### 证据轴

判据来源：`docs/architecture/test-validity-governance.md` 的六类合同来源与审核清单，`docs/architecture/test-evidence-governance.md` 的执行证据口径。

报告：

- 新增或修改的断言，在等价错误实现上会红吗——**要变异矩阵，否则鉴别力未知**
- 删除或弱化了负向断言的，说出真不变量了吗
- 结论的证明范围，有没有超过实际跑过的 provider、lane、拓扑、数据量
- PR body 声称跑过的，产物里有对应记录吗

工具陷阱：C# `const` 会被内联，增量构建下变异测试既会假绿也会假红。恢复变异后须 `--no-incremental` 全量重建；探针用类型引用而非 const。

## 3. 汇报

三轴**分列**，各给各的最严重项。跨轴排名会重新制造遮蔽，那正是分轴要避免的。

```markdown
## 标准
## 规格
## 证据
```

- 席位给的「某处不存在」「某处没做」这类判断，自己回 head 树核过再落笔
- 阻断项与建议分开
- 自己的 PR 只发 `COMMENT`
- 行内评论发完回读一次，确认锚在预期那行
- 中文发布，见语言治理

完成判据：每轴各有结论（含「本轴无发现」），且每条阻断项都能指到 head 树上的具体位置。

## 权威来源（读，不要复述）

路径均相对仓库根。技能的源目录（`skills/<name>/`）与安装目录（`.agents/skills/<name>/`）到根的深度差一层，文件相对写法必在一端解析错，所以这里不用 markdown 链接。

- `AGENTS.md` 与目标路径上的各级 `AGENTS.md` —— 开工与交付纪律
- `docs/architecture/test-validity-governance.md` —— 六类合同来源、红绿验证、删弱化负向测试的四条件、PR 结论格式
- `docs/architecture/test-evidence-governance.md` —— 执行数量、CI 状态、产物链接的报告口径
- `docs/architecture/document-language-governance.md` —— 协作文本发布门禁
- `docs/architecture/decision-record-governance.md` —— PR 触及 ADR 时的分层判据与取代规则

## 与其它技能的边界

`superpowers:requesting-code-review` 管作者侧怎么请审，`receiving-code-review` 管怎么处理收到的反馈；本技能管审核方。

那份技能的 `code-reviewer.md` 模板写着席位不得再派子代理——**该约束对席位成立，对协调者不成立**。本技能就是协调者，它派出三个席位。
