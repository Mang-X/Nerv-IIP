# 仓库布局说明

本文档定义 Nerv-IIP 仓库的顶层目录职责、放置规则与非目标，确保后续代码创建时不会因为目录漂移重新破坏边界。

## 目标

1. 让新成员能在最短时间内理解代码应该放在哪里。
2. 防止平台服务、前端应用、Connector Host、基础设施与文档混杂在同一层级。
3. 为后续脚手架创建、CI 编排与权限管理提供稳定的目录基线。

## 顶层结构

```text
Nerv-IIP/
  README.md
  docs/
    adr/
    architecture/
  frontend/
    apps/
      console/
      business-console/
      business-pda/
      design-system/
    packages/
      ui/
      ui-mobile/
      app-shell/
      api-client/
      auth/
      business-core/
  backend/
    services/
      Business/
    gateway/
    common/
    tests/
  connector-hosts/
  infra/
  scripts/
    lib/
```

## 顶层目录职责

### docs

- adr：不可轻易反转的架构决策。
- architecture：目录职责、上下文边界、生成链路、实施计划和领域模型说明。
- 文档是工程启动的第一优先级，任何重大结构调整必须先更新文档再改代码。

### frontend

- apps：真实前端应用入口。当前包含主平台 `console`、业务 PC 控制台 `business-console`、一线作业 PDA `business-pda` 和 `design-system` 文档/预览站。
- packages：共享 UI、移动 UI、API 客户端、壳层、认证复用和业务前端内核包。当前包含 `ui`、`ui-mobile`、`app-shell`、`api-client`、`auth` 和 `business-core`；`layer-base`、`layer-platform`、`shared-types` 等只作为长期边界预留，未出现真实复用前不得创建空包。
- frontend 不放后端工程、Connector Host 工程或部署脚本。
- 主平台控制台放在 `frontend/apps/console`；真实业务 CRUD 与业务工作流控制台放在 `frontend/apps/business-console`，不得把 MES/WMS/ERP/PDM/CMMS 等业务页面塞进主平台 console。

### backend

- services：平台领域服务，如 IAM、FileStorage、AppHub、Ops、Notification、AI Integration、Knowledge；业务平台扩展服务在单仓过渡阶段只能放在 `services/Business/{Context}` 下。当前业务服务包括 MasterData、ProductEngineering、Inventory、Quality、Mes、DemandPlanning、BarcodeLabel、Approval、Wms、IndustrialTelemetry、Maintenance、Erp 和 Scheduling。
- gateway：PlatformGateway、BusinessGateway 与前端聚合接口。PlatformGateway 只承载主平台控制面 facade；BusinessGateway 承载业务前端或业务移动端 facade。
- common：窄共享库，如 Contracts、Sdk、Caching、Observability、Testing。
- tests：后端测试项目与测试宿主。
- backend 不承载 Connector Host 与 Connector。

#### 服务内结构约定

- 每个平台 HTTP 服务目录内部默认采用 src 与 tests 风格。
- src 下默认是 .Web、.Domain、.Infrastructure 三个项目。
- Application 作为 .Web 项目内部目录存在，不默认拆成独立项目。
- Contracts 只在确有跨进程共享契约需求时才按需拆出；Sdk 只放公开客户端能力，不放服务端领域模型或 Infrastructure 实现。
- backend/tests 只放跨服务测试宿主与集成测试；服务自身测试优先放在各自目录下的 tests。
- 示例：backend/services/AppHub/src/Nerv.IIP.AppHub.Web、backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web、backend/services/AppHub/src/Nerv.IIP.AppHub.Domain、backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure。
- 业务平台扩展服务示例：backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web、backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain、backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web。
- 主平台服务不得引用 backend/services/Business 下的 Web、Domain、Infrastructure 项目；业务服务只能通过 Platform SDK、公开 Contracts、OpenAPI、IntegrationEvent 和 IAM 授权上下文消费主平台能力。
- Domain 项目不得引用查询、读模型或算法输出契约项目；公开 API DTO、跨服务 DTO 与算法契约应在 Web/Application 或 Infrastructure 边界映射为领域输入、领域快照或领域 fact。

### connector-hosts

- 单独承载 Connector Host 与各类 Connector。
- 与 backend 分开维护 solution，并通过 Platform SDK 与版本化公开契约与主平台协作；同一主版本内允许独立发布和小版本升级。
- 目标是把目标环境异构性隔离在平台服务之外。

### infra

- 平台级 Aspire AppHost、本地开发编排、Docker Compose 生成产物、依赖服务模板、观测栈配置、环境变量示例。
- 首期重点是 docker-compose.dev.yml、依赖服务最小配置、OpenTelemetry 接线示例；后续完整平台编排统一收敛到 infra/aspire 下的 AppHost。
- infra 可以保存部署目标模板和 overlay，但不保存真实客户密钥或环境私有配置。
- 不在 infra 中编写业务逻辑。

### scripts

- 统一放置初始化、环境校验、代码生成、安装、发布辅助脚本。
- 前后端脚手架脚本都应从这里暴露稳定入口，而不是散落在各自子目录。
- Windows/Linux 整合安装脚本归 scripts 管理；脚本只编排安装和运维动作，不承载业务规则。
- scripts/lib 放共享脚本 helper；脚本分类、副作用声明、超时、日志、进程清理和静态门禁按 docs/architecture/script-automation-governance.md 执行。

## 放置规则

1. 真实运行时代码必须放在 frontend、backend、connector-hosts 之一，禁止放在根目录。
2. 文档一律放在 docs，禁止把规范文本放进 scripts、infra 或任意 README 角落里替代正式文档。
3. 公共代码只有在明确服务多个消费者后才允许上提；否则优先留在服务或应用内部。
4. 共享库必须有狭窄职责，不能创建无边界的 SharedKernel、Common、Utils 聚合目录。
5. Connector Host 与 Connector 永远不并入 backend solution，防止平台核心服务与目标环境适配逻辑耦合。
6. 平台 HTTP 服务入口项目统一使用 .Web 命名；仅 Connector Host 宿主或后台进程类项目保留 .Host 命名。
7. 主平台代码不得引用 connector-hosts 下的项目；Connector Host 也不得引用 backend/services 或 backend/gateway 下的服务实现项目。
8. backend/common/Sdk 下的项目不得引用 backend/services 或 backend/gateway 下的 Web、Domain、Infrastructure 项目。
9. 每个平台服务不得各自创建长期维护的 Aspire AppHost；统一平台编排入口归 infra/aspire。
10. 业务平台扩展不得把 PDM/PLM、MPS/MRP、Scheduling/APS、MES、WMS、ERP、IIoT 或 CMMS 领域规则写入 PlatformGateway、IAM、AppHub、Ops 或主平台 console。
11. BusinessGateway 可以通过公开 HTTP 契约、OpenAPI DTO、Platform SDK、IAM 授权上下文和 internal service token 调用业务服务，但不得引用 `backend/services/Business` 下的 Web、Domain 或 Infrastructure 项目。
12. 服务间共享 DTO 必须通过 backend/common/Contracts 或 Sdk 的窄边界复用；调用方不得在本服务内长期复制其他服务的公开请求/响应 DTO。

## 非目标

1. 不在此文档中规定每个目录下的所有子目录细节。
2. 不在此文档中定义 CI 文件名与提交规范；脚本命名和治理细节见 docs/architecture/script-automation-governance.md。
3. 不在此文档中决定所有包是否立即创建，只定义未来代码放置的基线。
