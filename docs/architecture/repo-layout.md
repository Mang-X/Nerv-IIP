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
    packages/
  backend/
    services/
    gateway/
    common/
    tests/
  connector-hosts/
  infra/
  scripts/
```

## 顶层目录职责

### docs

- adr：不可轻易反转的架构决策。
- architecture：目录职责、上下文边界、生成链路、实施计划和领域模型说明。
- 文档是工程启动的第一优先级，任何重大结构调整必须先更新文档再改代码。

### frontend

- apps：真实前端应用入口。
- packages：共享 UI、共享类型、API 客户端、壳层能力、layer 包。
- frontend 不放后端工程、Connector Host 工程或部署脚本。

### backend

- services：平台领域服务，如 IAM、FileStorage、AppHub、Ops、Notification、AI Integration、Knowledge。
- gateway：PlatformGateway 与前端聚合接口。
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

### connector-hosts

- 单独承载 Connector Host 与各类 Connector。
- 与 backend 分开维护 solution，并通过 Platform SDK 与版本化公开契约与主平台协作；同一主版本内允许独立发布和小版本升级。
- 目标是把目标环境异构性隔离在平台服务之外。

### infra

- 本地开发编排、依赖服务模板、观测栈配置、环境变量示例。
- 首期重点是 docker-compose.dev.yml、依赖服务最小配置、OpenTelemetry 接线示例。
- 不在 infra 中编写业务逻辑。

### scripts

- 统一放置初始化、环境校验、代码生成、发布辅助脚本。
- 前后端脚手架脚本都应从这里暴露稳定入口，而不是散落在各自子目录。

## 放置规则

1. 真实运行时代码必须放在 frontend、backend、connector-hosts 之一，禁止放在根目录。
2. 文档一律放在 docs，禁止把规范文本放进 scripts、infra 或任意 README 角落里替代正式文档。
3. 公共代码只有在明确服务多个消费者后才允许上提；否则优先留在服务或应用内部。
4. 共享库必须有狭窄职责，不能创建无边界的 SharedKernel、Common、Utils 聚合目录。
5. Connector Host 与 Connector 永远不并入 backend solution，防止平台核心服务与目标环境适配逻辑耦合。
6. 平台 HTTP 服务入口项目统一使用 .Web 命名；仅 Connector Host 宿主或后台进程类项目保留 .Host 命名。
7. 主平台代码不得引用 connector-hosts 下的项目；Connector Host 也不得引用 backend/services 或 backend/gateway 下的服务实现项目。
8. backend/common/Sdk 下的项目不得引用 backend/services 或 backend/gateway 下的 Web、Domain、Infrastructure 项目。

## 非目标

1. 不在此文档中规定每个目录下的所有子目录细节。
2. 不在此文档中定义 CI 文件名、脚本命名细节与提交规范。
3. 不在此文档中决定所有包是否立即创建，只定义未来代码放置的基线。
