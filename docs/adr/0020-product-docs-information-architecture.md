# ADR 0020: 产品文档信息架构与内容治理

- Status: Accepted
- Date: 2026-07-07

## Context

产品文档站是 `frontend/apps/docs` 下的独立 VitePress 应用（#621 落地），当前承载三类内容：
三条端到端上手指南（#621）、六张核心业务流程图（#633）和 internal/gaps 内部缺口记录
（#634，即 MAN-333 建立的"文档走查 → 缺口证据 → 建议 issue"反查模式）。
`docs/architecture/implementation-readiness.md` 已冻结其定位：承载上手指南、核心业务流程
图和内部缺口记录，不把正文放入 Business Console。

产品页面尚未齐全，无法一次写完全流程文档；但文档设计方法论可以先行冻结，让后续内容与功能
批次同步推进，而不是每批各写各的。同时首批内容已经暴露三个结构性问题：

1. 现有导航按交付批次组织（"上手路径""流程图"），没有回答"我是谁、我该看什么"；
   internal/gaps/product-docs-overview.md 已把"按角色入口"记录为缺口。
2. 缺口反查目前只存在于 internal/gaps 的证据文件里，公开页面与 GitHub issue 之间没有
   制度化的引用关系，读者无法区分"还没写"和"产品还没做"。
3. 文档与功能演进之间没有同步机制，新页面/改流程的 PR 不会提示文档影响。

`frontend/apps/docs/src/product-docs.contract.test.ts` 已经把三条守护固化为测试：公开页
反引号路由必须真实存在于 business-console 页面、公开页禁止 demo/seed/mock 英文字样、
上手指南必须包含固定章节并链接内部缺口记录。本 ADR 的结构决策必须与该契约测试同步演进。

## Decision

### 1. 框架：Diátaxis 四象限

产品文档站按 Diátaxis 框架组织为四个象限，每个象限有独立目录与导航入口：

| 象限 | 回答的问题 | 目录 | 导航入口 |
| --- | --- | --- | --- |
| 教程（Tutorials） | 新手第一次如何走通一条主线 | `docs/getting-started/` | 教程 |
| 操作指南（How-to） | 某角色的某个任务怎么做 | `docs/how-to/` | 操作指南 |
| 概念解释（Explanation） | 业务为什么这样运作（含流程图/泳道图） | `docs/explanation/`（既有 `docs/processes/` 保留原路径并归入本象限） | 概念解释 |
| 参考（Reference） | 页面与字段字典，供查阅 | `docs/reference/` | 参考 |

归类规则：一篇文档只属于一个象限；教程不展开原理，参考不写操作步骤；流程图、泳道图和
状态机说明属于概念解释。已发布页面 URL 不迁移（`processes/` 留在原地），新内容一律按象限
目录落位，不允许在目录外新建散页。

现有文档逐篇归位映射表：

| 现有页面 | 归位象限 | 说明 |
| --- | --- | --- |
| `docs/index.md` | 站点首页（导航层） | 增加按角色入门入口与四象限结构说明 |
| `docs/getting-started/engineering-to-production.md` | 教程 | 章节中的"操作步骤/常见失败"兼具操作指南素材，后续按角色任务拆出 how-to 篇 |
| `docs/getting-started/planning-to-finished-goods.md` | 教程 | 同上 |
| `docs/getting-started/wms-inventory-cycle.md` | 教程 | 同上 |
| `docs/processes/index.md` | 概念解释 | 六张 Mermaid 流程图与 facade 映射表；URL 不迁移 |
| `docs/internal/gaps/*.md` | 不属于四象限 | 内部缺口证据，不进公开导航，继续按 MAN-333 模式维护 |

操作指南与参考象限当前只有索引页（占位），正文按第 5 节排期口径补齐。

### 2. 角色导向内容地图

在四象限之上提供角色入口层 `docs/roles/`，覆盖六个角色：计划员、班组长、仓管员、质检员、
设备工程师、采购与财务。角色页是导航层，不承载操作正文。

- 每个角色页给出"第一周要会的 N 条路径"；路径 = 一串页面操作达成一个业务结果。
- 每条路径标注可用性状态，口径冻结为三档：
  - **可用**：业务结果当前可以在系统里达成；
  - **部分可用**：结果可以达成，但关键子能力缺失（必须引用对应 GitHub issue）；
  - **缺口**：结果当前达不成（必须引用对应 GitHub issue）。
- 路径中的页面入口以 `frontend/apps/business-console/src/pages` 实际存在的路由为准，
  由契约测试强制。

缺口反查机制（MAN-333 模式）制度化为三层：

1. 公开页只写"当前限制"与状态标注，不出现内部证据措辞；
2. 文档走查发现的缺口先进 `internal/gaps/` 记录证据页面与建议 issue 标题；
3. 缺口回收成 GitHub issue 后，角色路径表直接引用 issue 编号；角色页只允许引用已存在的
   issue，不为尚未建 issue 的缺口编造引用。

工程资料维护员/工艺工程师等未列入首批六角色的读者，暂以教程象限的工程资料主线为入口；
扩充角色须更新本 ADR 的角色清单或在角色索引页显式标注。

### 3. 文档同步机制

- **PR 约定（轻量 checklist，非硬门禁）**：新增页面、修改业务流程或改变用户可见行为的
  PR，在描述中回答"是否影响产品文档（frontend/apps/docs）"；影响则同 PR 更新文档或引用
  后续补文档的 issue，不影响写"文档：无影响"。该约定写入 `AGENTS.md` 的 GitHub Workflow
  段，由评审习惯维持，不接 CI 硬门禁。
- **季度回收**：`internal/gaps/` 记录与角色路径表中的"部分可用/缺口"项每季度盘点一次，
  统一转成或更新 GitHub issue；已闭环的缺口同步把路径状态改回"可用"。
- **契约测试守护**：`product-docs.contract.test.ts` 随本 ADR 扩展——角色页必须包含
  "第一周路径"结构与状态标注，缺口标注必须携带 issue 引用，四象限索引页必须存在；
  docs 站内部路由前缀（roles/how-to/explanation/reference）加入 business-console 路由
  校验的豁免清单。

### 4. 演示数据策略

- 文档截图与示例统一使用 seed 演示数据，业务编号用真实感编号（如 `WO-`、`WC-`、SKU 码），
  不用占位符或随机字符串。
- 面向用户的文案禁止出现 mock/demo/seed 等英文开发字样（契约测试已强制含变体），也禁止
  "测试数据""假数据""演示占位"等中文开发口径；能力边界统一用"当前限制/当前缺口"表述。
- 依据 ADR 0009 的 seed 策略，截图数据应可由环境初始化复现，避免文档截图绑定某台开发机的
  临时状态。

### 5. 排期口径

- 本 ADR 与文档站导航 IA 重构（MAN-434 / #788）只交付：本决策、四象限+角色入口导航结构、
  角色路径地图与各象限占位索引页，不补操作指南与参考正文。
- 操作指南、参考字典和概念解释的增量正文随后续功能批次同步补齐，由第 3 节同步机制驱动；
  文档批次不单独立项，除非季度回收发现成规模欠账。

## Consequences

- `frontend/apps/docs` 导航与侧栏按"角色入口 + 四象限"重组；`getting-started/` 与
  `processes/` 的 URL 保持不变，外部链接不断。
- 新增 `roles/`（角色索引 + 六个角色页）、`how-to/`、`explanation/`、`reference/` 目录；
  操作指南与参考当前为诚实的占位索引，明确"正文随功能批次补"。
- `product-docs.contract.test.ts` 扩展角色地图与象限结构守护；后续文档 PR 触碰结构时
  必须让契约测试与本 ADR 保持一致。
- `AGENTS.md` GitHub Workflow 段新增 PR 文档影响 checklist 约定；执行力度依赖评审习惯，
  季度回收作为兜底。
- 角色路径表成为"产品当前能力"的用户可见口径之一，与 implementation-readiness 的工程
  口径并存：readiness 面向开发者记录服务事实，角色路径表面向用户回答"这条路今天能不能走通"，
  两者不一致时以代码事实为准并回改文档。
