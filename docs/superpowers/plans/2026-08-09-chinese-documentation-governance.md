# 全仓中文文档治理实施计划

> **供代理执行者使用：**必须使用 `superpowers:subagent-driven-development` 或等价的“独立实施 + 独立审核”流程逐项执行。所有步骤使用复选框跟踪。

**目标：**完成所有人工文档的简体中文治理，在根 `AGENTS.md` 固化仓库文档与 Linear 协作文本的中文要求，并通过反复清点消除全部未裁定的英文自然语言。

**实施结构：**控制代理从 Git 生成稳定清单，将文件按互斥路径和日期分区后分波次交给实施代理；审核代理与实施代理相互独立。并行代理共享工作树，因此只修改各自清单内的文件且不执行 Git 提交，控制代理在每轮审核通过后统一提交。

**技术栈：**Markdown、Git、Ruby/Awk 只读扫描、仓库现有 PowerShell 治理命令。

## 全局约束

- 人工叙述使用简体中文；代码、命令、路径、URL、标识符、配置键、协议字段、产品名及必要专业术语保留原文。
- 不改变数字、日期、状态、需求强度、代码块、链接目标、锚点、表格结构或技术含义。
- 测试夹具、日志、运行证据、指纹、快照、生成文件和需要保持字节稳定的机器输入不翻译。
- 根及子目录 `AGENTS.md` 的适用范围必须保持不变；子目录规则不得削弱根规则。
- Linear 的项目名称与说明、Issue 标题与正文、评论、审核意见、状态说明、验收记录和复盘结论默认使用中文；外部英文短引文必须附中文上下文或结论。
- 不修改业务代码、公开契约、数据库、生成客户端或 Linear 线上数据，不推送、不创建 PR。
- 实施代理不得审核自己的修改；审核发现的严重或重要问题必须回到原实施代理修复并再次复核。
- 并行实施代理不得执行 `git add`、`git commit`、格式化全仓文件或修改任务清单外的文件。

---

### Task 1：建立治理规则与权威清单

**文件：**

- 修改：`AGENTS.md`
- 创建：`docs/architecture/document-language-governance.md`
- 检查：全部 `AGENTS.md`、`CLAUDE.md` 与人工指令文件

**接口：**

- 输入：设计规格 `docs/superpowers/specs/2026-08-09-chinese-documentation-governance-design.md`
- 输出：全局语言规则、文件分类口径，以及后续任务使用的 Git 文件清单

- [ ] **步骤 1：从 Git 生成文档候选清单**

  运行：

  ```bash
  git ls-files | awk 'BEGIN{IGNORECASE=1} /(^|\/)(README|CHANGELOG|CONTRIBUTING|SECURITY|NOTICE|AGENTS|CLAUDE|GEMINI)(\.[^\/]*)?$|\.(md|mdx|markdown|txt|rst|adoc)$/{print}' | sort
  ```

  预期：候选清单覆盖根目录、`docs/`、`frontend/`、后端说明、`.claude/`、`skills/` 与 `infra/`。

- [ ] **步骤 2：分类不可翻译的机器输入**

  将测试夹具、日志、证据、指纹、快照和生成文件列入治理文档的排除表，每项写明路径模式与不可改写原因；不得用宽泛目录把人工文档一并排除。

- [ ] **步骤 3：中文化根 AGENTS 并加入语言治理章节**

  翻译 `AGENTS.md` 中所有英文自然语言，保留代码、命令和标识符。新增“文档与协作语言”章节，逐字覆盖全局约束中的仓库文档及 Linear 要求。

- [ ] **步骤 4：编写权威治理文档**

  `docs/architecture/document-language-governance.md` 必须包含治理范围、保留规则、排除表、扫描分类、实施/审核角色隔离和完成条件，不建立会把专业术语误判为失败的简单字符比例门禁。

- [ ] **步骤 5：验证本任务**

  运行：

  ```bash
  git diff --check -- AGENTS.md docs/architecture/document-language-governance.md
  rg -n '^#{1,6} +[A-Za-z][A-Za-z ]+$' AGENTS.md docs/architecture/document-language-governance.md
  ```

  预期：`git diff --check` 退出 0；第二条命令的命中均为允许保留的专业名词或产品名，否则继续翻译。

### Task 2：治理根目录、架构、ADR、后端、基础设施与技能文档

**文件：**

- 修改：根目录人工 Markdown（排除任务 1 已拥有的 `AGENTS.md`）
- 修改：`.claude/**/*.md`
- 修改：`docs/architecture/**/*.md`（排除任务 1 创建的治理文档）
- 修改：`docs/adr/**/*.md`、`docs/demo/**/*.md`
- 修改：`backend/**/README.md`、`backend/**/.github/**/*.md`、`infra/**/*.md`、`skills/**/*.md`

**接口：**

- 输入：任务 1 的语言规则和排除表
- 输出：非前端、非历史计划/规格区域的中文人工文档

- [ ] **步骤 1：生成本任务精确清单并逐文件确认人工属性**
- [ ] **步骤 2：翻译代码围栏外的标题、段落、列表和表格叙述**
- [ ] **步骤 3：保持命令、模板占位符、路径、代码示例和链接目标不变**
- [ ] **步骤 4：逐文件检查围栏开闭、表格列数和规范性词义**
- [ ] **步骤 5：运行 `git diff --check` 并在实施报告列出已改文件、排除文件及原因**

### Task 3：治理 2026 年 5 月 14 日至 5 月 23 日的历史实施计划

**文件：**

- 修改：`docs/superpowers/plans/2026-05-14-*.md` 至 `docs/superpowers/plans/2026-05-23-*.md`

**接口：**输入全局语言规则；输出该日期范围内全部中文化计划。

- [ ] **步骤 1：用 `git ls-files 'docs/superpowers/plans/2026-05-*.md'` 生成清单并只保留 14–23 日**
- [ ] **步骤 2：翻译目标、架构、任务说明、步骤、预期结果和提交说明中的自然语言**
- [ ] **步骤 3：保持命令、代码块、文件路径、函数签名和固定提交消息不变**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/plans` 并报告文件数**

### Task 4：治理 2026 年 5 月 24 日至 6 月 30 日的历史实施计划

**文件：**

- 修改：`docs/superpowers/plans/2026-05-24-*.md` 至 `2026-05-31-*.md`
- 修改：`docs/superpowers/plans/2026-06-*.md`

**接口：**输入全局语言规则；输出该日期范围内全部中文化计划。

- [ ] **步骤 1：从 Git 生成上述两个日期范围的精确清单**
- [ ] **步骤 2：逐文件翻译自然语言，保留所有技术字面量**
- [ ] **步骤 3：核对任务编号、复选框、代码围栏和链接目标未改变**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/plans` 并报告文件数**

### Task 5：治理 2026 年 7 月 1 日至 7 月 20 日的历史实施计划

**文件：**

- 修改：`docs/superpowers/plans/2026-07-01-*.md` 至 `docs/superpowers/plans/2026-07-20-*.md`

**接口：**输入全局语言规则；输出该日期范围内全部中文化计划。

- [ ] **步骤 1：从 Git 生成 7 月 1–20 日精确清单**
- [ ] **步骤 2：逐文件翻译自然语言并保持业务状态、编号和验收强度**
- [ ] **步骤 3：核对表格、命令、代码与链接**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/plans` 并报告文件数**

### Task 6：治理 2026 年 7 月 21 日至 8 月的历史实施计划

**文件：**

- 修改：`docs/superpowers/plans/2026-07-21-*.md` 至 `docs/superpowers/plans/2026-07-31-*.md`
- 修改：`docs/superpowers/plans/2026-08-*.md`，排除本实施计划自身

**接口：**输入全局语言规则；输出该日期范围内全部中文化计划。

- [ ] **步骤 1：从 Git 生成上述日期范围清单并排除 `2026-08-09-chinese-documentation-governance.md`**
- [ ] **步骤 2：逐文件翻译自然语言并保持技术证据、命令和标识符**
- [ ] **步骤 3：核对引用的 Issue/PR 编号、日期和验收结果未变化**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/plans` 并报告文件数**

### Task 7：治理 2026 年 5 月的历史设计规格

**文件：**

- 修改：`docs/superpowers/specs/2026-05-*.md`

**接口：**输入全局语言规则；输出 5 月全部中文化规格。

- [ ] **步骤 1：从 Git 生成 5 月规格清单**
- [ ] **步骤 2：翻译背景、目标、设计、约束、失败处理和验收说明**
- [ ] **步骤 3：保持接口、类型、路由、事件名、代码和数据结构不变**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/specs` 并报告文件数**

### Task 8：治理 2026 年 6 月至 7 月 15 日的历史设计规格

**文件：**

- 修改：`docs/superpowers/specs/2026-06-*.md`
- 修改：`docs/superpowers/specs/2026-07-01-*.md` 至 `2026-07-15-*.md`

**接口：**输入全局语言规则；输出该日期范围内全部中文化规格。

- [ ] **步骤 1：从 Git 生成精确清单**
- [ ] **步骤 2：逐文件翻译自然语言，保持所有契约字面量与技术证据**
- [ ] **步骤 3：核对表格、状态机、序列和边界条件**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/specs` 并报告文件数**

### Task 9：治理 2026 年 7 月 16 日至 8 月的历史设计规格

**文件：**

- 修改：`docs/superpowers/specs/2026-07-16-*.md` 至 `docs/superpowers/specs/2026-07-31-*.md`
- 修改：`docs/superpowers/specs/2026-08-*.md`，排除已中文化的本治理设计

**接口：**输入全局语言规则；输出该日期范围内全部中文化规格。

- [ ] **步骤 1：从 Git 生成精确清单并排除本治理设计**
- [ ] **步骤 2：逐文件翻译自然语言，保持证据、标识符、日期与需求强度**
- [ ] **步骤 3：检查所有引用与围栏**
- [ ] **步骤 4：运行 `git diff --check -- docs/superpowers/specs` 并报告文件数**

### Task 10：治理设计系统桌面组件文档

**文件：**

- 修改：`frontend/apps/design-system/docs/components/desktop/**/*.md`
- 修改：`frontend/apps/design-system/docs/components/board.md`
- 修改：`frontend/apps/design-system/docs/components/overview.md`

**接口：**输入全局规则与 NvUI 命名约束；输出桌面组件中文文档。

- [ ] **步骤 1：读取组件真实 props 与现有示例，生成目标清单**
- [ ] **步骤 2：翻译用途、属性说明、示例说明、Do/Don't 和无障碍说明**
- [ ] **步骤 3：保持组件名、props、事件、插槽、import 和代码示例不变**
- [ ] **步骤 4：运行 `git diff --check -- frontend/apps/design-system/docs` 并报告文件数**

### Task 11：治理设计系统移动、大屏、触屏、基础与指南文档

**文件：**

- 修改：`frontend/apps/design-system/docs/components/mobile/**/*.md`
- 修改：`frontend/apps/design-system/docs/components/screen/**/*.md`
- 修改：`frontend/apps/design-system/docs/components/touch/**/*.md`
- 修改：`frontend/apps/design-system/docs/foundations/**/*.md`
- 修改：`frontend/apps/design-system/docs/guide/**/*.md`
- 修改：`frontend/apps/design-system/docs/patterns/**/*.md`
- 修改：`frontend/apps/design-system/docs/index.md`

**接口：**输入全局规则与各表面 AGENTS 约束；输出非桌面设计系统中文文档。

- [ ] **步骤 1：按表面生成目标清单**
- [ ] **步骤 2：翻译自然语言并保留 NvUI 组件、props、token 和代码示例**
- [ ] **步骤 3：核对大屏、移动和触屏术语没有跨表面混用**
- [ ] **步骤 4：运行 `git diff --check -- frontend/apps/design-system/docs` 并报告文件数**

### Task 12：治理其余前端设计、产品文档与子树指令

**文件：**

- 修改：`frontend/DESIGN/**/*.md`
- 修改：`frontend/apps/docs/docs/**/*.md`
- 修改：前端所有 `README.md`、`DESIGN.md`、`AGENTS.md`、`CLAUDE.md`
- 修改：`frontend/packages/ui/src/components/**/product.md` 与 `MIGRATION.md`

**接口：**输入全局规则与所有前端子树 AGENTS；输出其余前端人工文档的中文版本。

- [ ] **步骤 1：生成精确清单并确认不包含设计系统任务 10/11 已拥有的文件**
- [ ] **步骤 2：翻译自然语言；子树 AGENTS 仅翻译，不改变其覆盖关系和硬约束**
- [ ] **步骤 3：保持组件名、token、命令、路由、代码和资源路径不变**
- [ ] **步骤 4：运行 `git diff --check -- frontend` 并报告文件数**

### Task 13：全仓重新清点、修复循环与最终验证

**文件：**

- 检查：任务 1 的完整候选清单
- 修改：仅修改扫描后判定为“必须翻译”或审核确认受损的人工文档

**接口：**输入任务 1–12 的全部结果；输出零未裁定项的最终清单和验证证据。

- [ ] **步骤 1：从当前 Git 状态重新生成候选清单**

  重新运行任务 1 的清单命令并统计文件数、扩展名和目录分布；新增或删除的候选必须解释。

- [ ] **步骤 2：扫描代码围栏外的英文自然语言**

  使用只读扫描提取无中文上下文的英文标题、连续英文段落和表格说明，逐项裁定为“必须翻译”“合法保留”或“机器输入排除”。不得仅凭字符比例自动放行。

- [ ] **步骤 3：分派独立修复代理处理全部“必须翻译”项**

  修复后由新的审核代理复核；重复步骤 1–3，直到“必须翻译”和“未裁定”均为 0。

- [ ] **步骤 4：检查 Markdown 结构与差异卫生**

  运行：

  ```bash
  git diff --check
  git status --short
  ```

  对所有改动 Markdown 检查围栏成对、内部相对链接目标存在、表格分隔行完整；任何失败均回到修复循环。

- [ ] **步骤 5：检查治理规则覆盖**

  读取全部 `AGENTS.md`，确认根规则明确覆盖文档和 Linear 协作文本，子目录没有冲突或弱化。

- [ ] **步骤 6：独立全分支复核**

  审核完整分支差异，按严重程度报告文件与行号、具体场景、修复建议和确定性。所有严重或重要问题进入一次集中修复和再次复核。

- [ ] **步骤 7：记录最终证据并统一提交**

  只有在新鲜扫描、结构检查、`git diff --check` 和独立复核均满足完成条件后，才提交最终治理结果；不推送、不创建 PR。
