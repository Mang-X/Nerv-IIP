# 领导演示范围冻结 v1

本文档是领导演示范围的版本化、可审计基线。它只冻结演示案例、顺序、讲稿口径和延期边界，不证明尚未验收的业务能力已经交付，也不授权创建演示专用假接口、假 seed 或跨服务关联。

| 字段 | 值 |
| --- | --- |
| 版本 | `1.0` |
| 冻结日期 | 2026-07-18 |
| 治理入口 | [MAN-525](https://linear.app/mangax/issue/MAN-525/p0m0-%E8%8C%83%E5%9B%B4%E5%86%BB%E7%BB%93%E4%B8%8E%E7%8A%B6%E6%80%81%E6%A0%A1%E6%AD%A3-checklist) / [GitHub #966](https://github.com/Mang-X/Nerv-IIP/issues/966) |
| 适用范围 | 领导演示的 seed、验收脚本、页面走查和讲稿 |

## 变更控制

1. 本文档是 v1 唯一范围基线。后续变更必须通过仓库 PR，更新版本号和文末变更记录，并在 MAN-525 或其明确的后继治理 issue 留下链接。
2. 三套案例编号在 v1 内不可改名。seed、脚本、页面默认值和讲稿只能引用这些编号；不存在的数据不得用临时假 seed 或相似编号冒充。
3. 页面顺序是讲述顺序，不是能力完成声明。页面、关联键或自动流转尚未验收时，现场必须按本文的缺口口径说明或跳过，不得口头补全。
4. 在线 issue、PR 与代码事实优先于本文的历史快照。事实变化必须先更新证据，再调整演示口径。

## M0 checklist 与证据

以下勾选只表示对应的**范围治理动作**已经冻结；关联业务 issue 是否完成，以“未完成能力与责任 issue”一节为准。

- [x] **连接器健康进入现场。** 使用只读采集健康卡片墙 `/equipment/telemetry/connectors`，并允许展开已配置 tag coverage。PR [#929](https://github.com/Mang-X/Nerv-IIP/pull/929) 交付卡片墙；[#947](https://github.com/Mang-X/Nerv-IIP/issues/947) 与 [#951](https://github.com/Mang-X/Nerv-IIP/issues/951) 已由 PR [#952](https://github.com/Mang-X/Nerv-IIP/pull/952) 合并关闭；原验收 issue [#796](https://github.com/Mang-X/Nerv-IIP/issues/796) 当前也已关闭。现场时效口径见下文，不沿用“现有协议不可满足”的旧结论。
- [x] **demo 标签口径已校齐。** [#890](https://github.com/Mang-X/Nerv-IIP/issues/890) 与 [#878](https://github.com/Mang-X/Nerv-IIP/issues/878) 当前均为 OPEN、`demo:optional`，不属于必演主链。
- [x] **三套固定案例编号已冻结。** 编号和用途见下表；仓库当前没有为本治理项创建 seed 或接口。
- [x] **页面顺序与讲稿骨架已冻结。** 固定为下文 12 步；断链结论由 [MAN-524](https://linear.app/mangax/issue/MAN-524/p0integration-%E4%B8%BB%E9%93%BE%E7%AB%AF%E5%88%B0%E7%AB%AF%E5%86%92%E7%83%9F%E8%B5%B0%E6%9F%A5%E7%8E%B0%E6%9C%89%E7%AB%AF%E7%82%B9%E4%B8%B2%E9%80%9A%E9%94%80%E5%94%AE%E4%BA%A4%E4%BB%98%E6%9A%B4%E9%9C%B2%E6%96%AD%E9%93%BE%E8%B7%B3) / [#965](https://github.com/Mang-X/Nerv-IIP/issues/965) 提供，不猜测关联。
- [x] **状态事实已校正。** [MAN-440](https://linear.app/mangax/issue/MAN-440/p1console-c1-3-%E8%AE%BE%E5%A4%87%E8%BF%90%E8%A1%8C%E5%B0%8F%E6%97%B6%E6%8C%87%E6%A0%87-%E4%BF%9D%E5%85%BB%E8%AE%A1%E5%88%92%E8%A7%A6%E5%8F%91%E6%A8%A1%E5%BC%8F%E5%88%87%E6%8D%A2) / [#794](https://github.com/Mang-X/Nerv-IIP/issues/794) 已收窄为真机端到端验收，MAN-440 当前保持 `Todo`；PR [#931](https://github.com/Mang-X/Nerv-IIP/pull/931) 已合并，[MAN-445](https://linear.app/mangax/issue/MAN-445/p1console-c2-3-%E5%AE%8C%E5%B7%A5%E5%85%A5%E5%BA%93%E5%A4%B1%E8%B4%A5%E9%87%8D%E8%AF%95-%E8%B4%A8%E9%87%8F-hold-%E6%97%B6%E9%97%B4%E7%BA%BF%E5%8F%AF%E8%A7%86%E5%8C%96) 已从失真的 `In Review` 校正为 `Todo`；[#799](https://github.com/Mang-X/Nerv-IIP/issues/799) 仍保持 OPEN，未完成验收由 [#954](https://github.com/Mang-X/Nerv-IIP/issues/954) 承接。
- [x] **主链冒烟走查已建并排入 M1。** MAN-524 / [#965](https://github.com/Mang-X/Nerv-IIP/issues/965) 当前在 M1 且已开始，结论作为 MAN-518 / [#959](https://github.com/Mang-X/Nerv-IIP/issues/959) 的范围输入。
- [x] **延期边界已冻结。** [#954](https://github.com/Mang-X/Nerv-IIP/issues/954)、[#875](https://github.com/Mang-X/Nerv-IIP/issues/875)、[#924](https://github.com/Mang-X/Nerv-IIP/issues/924) 当前均为 OPEN、`demo:defer`；现场不演示、不口头承诺。

## 三套固定案例

| 案例 | 固定编号 | 演示用途 | 边界 |
| --- | --- | --- | --- |
| A：销售到履约 | `SO-DEMO-001` | 销售、计划、生产、质量、库存、发货和财务主线的锚点 | 只展示 MAN-524 已证明存在稳定关联键的节点；其余节点明确显示“尚未建立关联”或跳过。 |
| B：质量保留与完工入库 | `WO-DEMO-Q01` | 完工入库失败重试、质量 hold 时间线与人工理由释放 | 不演示“同源不良→复检合格→自动释放”；该闭环由 #954 承接。 |
| C：设备与保养 | `DEV-CNC-DEMO`、`MWO-DEMO-001` | 设备采集健康、运行事实和保养工单的可见链路 | 不演示真实 PLC 控制回执或 PDA 照片持久化；分别由 #875、#924 承接。 |

## 固定页面与讲稿顺序

每一步只讲当前页面能由真实代码和数据证明的事实。上一步到下一步没有稳定关联时，必须明确说“该跳仍在冒烟走查”，不得用编号相似性串联。

| 顺序 | 页面/段落 | 讲稿骨架 |
| ---: | --- | --- |
| 1 | 经营总览 | 先说明演示目标、三套固定案例与当前范围；总览指标只按页面实际数据源解释。 |
| 2 | 销售 | 以 `SO-DEMO-001` 为主线起点，确认订单状态和真实业务编号。 |
| 3 | 计划 / MRP | 展示已经存在的需求、运行或建议事实；是否可回溯销售订单以 MAN-524 证据为准。 |
| 4 | 工程准备 | 只展示真实物料、版本、工艺或准备状态，不把缺失准备事实说成已完成。 |
| 5 | APS | 展示真实排程方案、约束和冲突；不承诺未交付的全局优化或自动重排。 |
| 6 | MES | 展示工单和执行事实；质量案例固定使用 `WO-DEMO-Q01`。 |
| 7 | 质量 | 展示现有检验、NCR 或 hold 事实；同源复检自动释放明确延期到 #954。 |
| 8 | 设备 | 展示 `DEV-CNC-DEMO` 的运行/采集事实和 `MWO-DEMO-001` 的保养工单；控制回执不进入现场。 |
| 9 | 入库 / 批次 | 展示真实完工入库、库存或批次事实；失败重试仅在真实状态链可复现时演示。 |
| 10 | 发货 / 出库 | 展示真实发货与 WMS 出库事实；ERP→WMS 自动链是否成立以 MAN-524 结论为准。 |
| 11 | 应收 / 凭证 | 展示真实应收与凭证聚合；自动还是手工创建必须按冒烟证据说明。 |
| 12 | 履约总览 | 回到 `SO-DEMO-001` 汇总已证实节点；未建立稳定关联的节点保持显式缺口。 |

履约总览结束后只复述已证明的主链、当前卡点和三项延期边界，不追加页面或路线图承诺。

## 连接器健康现场口径

现场统一说法：

> 采集健康页把 Host 存活、现场协议连接、采集循环健康和 tag 样本存在性作为四个独立事实呈现。当前受治理的 AppHost 验收 profile 中，页面每 10 秒刷新；真实 Modbus 拔线验收连续 3/3 通过，端到端可见最大 3181ms，因此可在下一轮页面刷新看到断线。这个时效只适用于当前验收 profile，不泛化到任意部署参数。

禁止说法：

- 不把“最近没有样本”或 collector error 直接说成现场断线。
- 不把 Host heartbeat 说成 PLC、Modbus 或 MQTT 现场连接仍然存活。
- 不把 tag sample presence 说成数据质量合格或数据仍然新鲜。
- 不承诺任意部署都满足固定 10 秒；改变 heartbeat、probe、backend deadline 或页面轮询配置后必须重新验收。

证据：[PR #952 验证记录](https://github.com/Mang-X/Nerv-IIP/pull/952)、[Connector Protocol V1](./connector-platform-protocol-v1.md#canonical-identity连接状态与-tag-manifest-扩展947--951)、[设备工程师产品说明](../../frontend/apps/docs/docs/roles/equipment-engineer.md#采集健康排查)。

## demo 标签口径

| 标签 | 演示含义 | 进入现场的规则 |
| --- | --- | --- |
| `demo:must` | 已纳入主线验收范围 | 彩排前必须有真实证据；存在未验收项时保持开启并明确责任 issue。 |
| `demo:optional` | 可选增强，不是领导演示完成条件 | 只有真实数据和环境稳定时才展示；不得占用主线或转化成口头承诺。 |
| `demo:defer` | 本版明确延期 | 不演示、不用假数据替代、不口头承诺交付时间。 |
| `demo:blocker` | 会阻断当前演示目标的未决事实 | 必须在彩排前关闭，或由治理决策显式降级/延期并留下 issue 证据。 |
| `demo:evidence` | 为演示结论提供验收证据 | 输出可审计记录；本身不等于业务能力完成。 |

## 未完成能力与责任 issue

这些项目**没有**因 M0 治理 checklist 勾选而完成：

| 未完成事实 | 当前口径 | 责任 issue |
| --- | --- | --- |
| 同源不良经复检合格后自动释放 MES quality hold | `demo:defer`，不进入现场；不得用 seed hold 冒充 | [#954](https://github.com/Mang-X/Nerv-IIP/issues/954)，并继续阻塞 [#799](https://github.com/Mang-X/Nerv-IIP/issues/799) 的最终验收 |
| 运行小时型保养计划在真实 Postgres + Redis 链路自动生成工单 | `demo:optional`，端到端验收未完成，MAN-440 保持 `Todo` | [#794](https://github.com/Mang-X/Nerv-IIP/issues/794) / MAN-440 |
| 销售到交付每一跳的稳定关联键与自动/手工结论 | `demo:blocker`，只展示已证实的跳 | [#965](https://github.com/Mang-X/Nerv-IIP/issues/965) / MAN-524 |
| 销售订单履约追踪主视觉 | 以后续冒烟结论收窄范围，不在 M0 文档 PR 实现 | [#959](https://github.com/Mang-X/Nerv-IIP/issues/959) / MAN-518 |
| 真实 PLC 控制回执 | `demo:defer`，不进入现场 | [#875](https://github.com/Mang-X/Nerv-IIP/issues/875) |
| PDA 点检照片持久化 | `demo:defer`，不进入现场 | [#924](https://github.com/Mang-X/Nerv-IIP/issues/924) |

## 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| 1.0 | 2026-07-18 | 首次冻结 MAN-525 / #966 的案例、顺序、现场口径、标签语义和延期边界。 |
