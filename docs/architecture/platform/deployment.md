# 平台部署架构

本文只描述 Nerv-IIP **当前部署拓扑、组件边界与配置所有权**。部署规则见 [`../../governance/delivery/`](../../governance/delivery/)，实际操作见 [`../../runbooks/deployment.md`](../../runbooks/deployment.md)。历史阶段、运行证据和旧方案不属于本文。

## 权威来源

易变化事实不得在本文形成第二份清单：

- 平台拓扑、资源依赖、环境注入：`infra/aspire/Nerv.IIP.AppHost/Program.cs`。
- 根命令与可用参数：`nerv.ps1 help`。
- Compose 生成/准备/部署行为：`nerv.ps1` 与 `infra/aspire/README.md`。
- legacy Compose 迁移资产：`infra/compose/`。
- 安装、迁移与打包入口：`scripts/install/`、`scripts/package/`。
- 观测边界：[`observability.md`](observability.md)。
- 长期部署决策：[`../../adr/0008-multi-target-deployment-and-aspire-apphost.md`](../../adr/0008-multi-target-deployment-and-aspire-apphost.md)。

当文档与上述 producer 冲突时，以 producer 为准并修正文档。

## 拓扑所有权

### 平台级 AppHost

`infra/aspire/Nerv.IIP.AppHost` 是平台服务、基础设施依赖、连接关系、环境注入和 Aspire deployment target 的唯一平台级拓扑模型。

领域服务不拥有各自的竞争性 AppHost；AppHost 也不拥有 IAM、AppHub、Ops、FileStorage 或业务域规则。新增服务、依赖或客户 profile 需要进入平台拓扑时，应先进入同一 AppHost，再由相应 delivery target 消费。

### 部署目标

| 目标 | 当前定位 | 拓扑来源 |
| --- | --- | --- |
| Aspire | 本地开发、联调、资源诊断与部署模型 | 平台级 AppHost |
| 联网 bootstrap | 新开发机、联网实施机、PoC 的工具链与本地开发准备 | `nerv.ps1` → `scripts/bootstrap-online.ps1` |
| Docker Compose | PoC、容器化私有化、发布演练 | Aspire deployment target 生成产物 |
| legacy Compose | 依赖兜底、迁移期验证和既有发布演练 | `infra/compose/`；不是完整平台服务图 |
| 安装/交付制品 | 无容器或传统运维环境的交付单元 | `scripts/package/`、`scripts/install/` 与服务发布产物 |

`infra/docker-compose.dev.yml` 只承担本地依赖兜底，不是平台服务拓扑来源。`infra/compose/` 下的 legacy overlay 可以继续服务既有验证，但不得扩张成第二套完整平台图。

### legacy MES 库存链路限制

`infra/compose/nerv-iip.platform.yml` 不下发以下仓储位置配置，因此不支持这些链路：

- **MES→WMS 领料：**不下发 `MaterialIssue__*`。领料事件未携带来源/线边库位且部署未配置时，WMS 在站点解析成功后将消息写入 `unresolved-location` 死信；站点本身无法唯一解析时先进入 `unresolved-site`。失败分支以 `MesMaterialIssueRequestedIntegrationEventHandler` 为准。
- **MES 线边收料：**不下发 `Inventory__SiteCode`、`Inventory__SourceLocationCodes__N`、`Inventory__LineSideLocationCode`。`InventoryMesMaterialSupplyLocationResolver` 在站点、来源候选库位或线边库位缺失时抛出 `MATERIAL_SUPPLY_LOCATION_UNCONFIGURED`。
- **MES 完工入库：**不下发 `Inventory__SiteCode`、`Inventory__FinishedGoodsLocationCode`。`ConfiguredMesFinishedGoodsReceiptLocationResolver` 在成品仓站点或库位缺失时抛出 `FINISHED_GOODS_LOCATION_UNCONFIGURED`；独立成品仓站点的配置覆盖键为 `Inventory:FinishedGoodsSiteCode`。

完整平台拓扑使用平台级 AppHost 生成的 Aspire Compose 产物，操作见 [`../../runbooks/deployment.md`](../../runbooks/deployment.md)。位置配置及非 Development 环境门控仍以 AppHost、MES/WMS 的配置绑定和上述 resolver/handler 为准。

## 服务与配置边界

1. 服务间 HTTP 地址是部署输入。当前服务继续消费既有 `Xxx:BaseUrl` 配置键；AppHost、生成的 Compose 和安装入口负责提供环境对应值，服务代码不能把非 Development 环境静默回退到 localhost。
2. 基础设施选择、消息 provider、数据库连接、内部认证材料、Connector Host scope 与环境差异由部署 profile 注入；精确键名、默认值和 fail-fast 行为以 AppHost、服务配置与安装脚本为准。
3. 数据库 schema 迁移属于服务自己的 migrations/migrator 边界，部署拓扑只编排其依赖；发布操作必须遵循 [`../../runbooks/database-release.md`](../../runbooks/database-release.md)。
4. 观测资源可以由 AppHost、Compose 或安装 profile 接入，但日志、trace、metric 与业务/审计事实仍保持独立边界，详见 [`observability.md`](observability.md)。
5. 安装包和脚本可以是不同交付入口，但必须消费同一部署模型与配置语义，不能发明只存在于某个脚本中的隐式服务图或配置键。

## Connector Host 边界

Connector Host 是独立分发和生命周期单元：可以与主平台同机，也可以部署在受管节点；必须支持独立安装、升级和回滚。Connector 适配能力留在 Connector Host 侧，通过版本化协议、Platform SDK、公开 API 与 IAM 授权关系连接主平台，不进入平台领域服务实现。

## 环境与生成产物

Development 与非 Development 的差异由 AppHost 环境、部署 profile 和服务自身启动守卫共同决定。生成的 Compose、安装包和现场配置只是同一拓扑在不同 delivery target 下的物化结果；它们不是新的架构事实来源。

对具体服务数量、镜像版本、数据库数量、端口、secret 参数、演示 seed 或一次性验收开关，不在本文冻结副本；需要精确值时回到 AppHost、`nerv.ps1`、安装脚本和目标服务配置。

## 不属于本文

- CI job、测试分片、构建参数和历史测量：[`../../governance/delivery/ci-build.md`](../../governance/delivery/ci-build.md) 与冻结审计报告。
- secret、发布、Compose 和安装行为约束：[`../../governance/delivery/deployment.md`](../../governance/delivery/deployment.md)。
- bootstrap、Compose 发布、部署、恢复和排障命令：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)。
- M2-J 迁移前的混合部署基线与时点事实：[`../../reports/audits/deployment-baseline-pre-m2-j-2026-09-07.md`](../../reports/audits/deployment-baseline-pre-m2-j-2026-09-07.md)。