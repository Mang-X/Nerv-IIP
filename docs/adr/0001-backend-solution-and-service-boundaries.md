# ADR 0001: 后端 solution 结构与服务边界

- Status: Accepted
- Date: 2026-05-13

## Context

Nerv-IIP 后端需要同时承载身份与权限、对外授权、文件存储、应用目录与实例发现、运维编排、AI 工具治理、知识检索以及前端聚合接口。0 到 1 阶段既不能把所有平台能力压成单体，也不能过早兑现成完全独立部署的高运维成本微服务群。

项目已决定以 netcorepal-cloud-framework 作为后端主框架，但必须保证领域边界优先于框架能力堆叠。团队需要一个既能支持后续服务拆分，又能在早期维持开发效率的 solution 组织方式。过宽的共享库会快速侵蚀服务边界，导致项目重新退化成逻辑耦合的大单体。

## Decision

1. 后端平台服务采用单一 solution 管理多服务代码库，根目录固定为 backend/Nerv.IIP.sln、Directory.Build.props、Directory.Packages.props、services、gateway、common、tests。
2. 逻辑边界固定为 IAM、File Storage、AppHub、Ops、Notification、AI Integration、Knowledge、Gateway/BFF、Connector Host 九类能力；其中 IAM 统一拥有平台身份、权限和对外授权事实，File Storage 统一拥有平台文件元数据、访问授权和对象存储治理事实，Notification 统一拥有平台通知、待办、接收人解析、偏好、去重和投递状态事实。
3. Connector Host 与 Connector 物理上保持在独立的 connector-hosts 根目录中，不并入 backend solution。
4. services 下按服务拆目录；每个平台 HTTP 服务默认采用 src 与 tests 目录，并在 src 下以 Web、Domain、Infrastructure 三个项目为主。
5. Application 作为 Web 项目内部目录存在，而不是默认独立项目；Commands、Queries、DomainEventHandlers、IntegrationEventHandlers 等应用层代码放在 Web/Application 下。
6. Contracts 不作为每个服务的默认项目层，只有在确有跨进程共享 DTO、协议或 SDK 抽象时才按需拆出。
7. gateway 单独承载 PlatformGateway，用于前端聚合查询、上下文透传与页面级 BFF 接口；默认采用薄 Web 服务，而不是预设为重型多层项目拆分。
8. common 只允许沉淀 Contracts、Sdk、Caching、Hosting、Observability、Testing 等窄共享库，禁止创建 SharedKernel、Common、Utils 一类无边界聚合库。
9. 各服务拥有独立的数据边界与契约边界，禁止通过共享数据库表实现跨服务协作。
10. 物理部署允许在早期合并，但代码边界、数据库边界与契约边界不因部署合并而回退。

## Rationale

1. 单一 solution 能降低 0 到 1 阶段的协作与调试成本，同时保留服务级边界。
2. 采用更贴近 netcorepal-cloud-framework 官方项目结构的 Web、Domain、Infrastructure 三项目主线，能减少框架落地时的额外适配成本。
3. 将 Application 保持在 Web 项目内部目录中，既能保留命令、查询、事件处理器的职责分区，也能避免为早期服务引入过多项目数量。
4. 将 Gateway/BFF 从领域服务中独立出来，可以避免页面聚合逻辑反向侵入服务边界。
5. 对 common 实施窄共享约束，可以从制度上抑制跨服务耦合蔓延。
6. 先冻结逻辑边界，再保留物理部署弹性，能在可演进性和早期交付速度之间取得平衡。

## Consequences

1. 早期项目数量会比传统单体更多，初始化与模板维护成本更高。
2. 团队需要持续进行边界治理，否则 common 和 gateway 容易重新长成隐形大单体。
3. 某些跨服务改动会显得更繁琐，但这是为了换取后续演进时的清晰边界。
4. 即使开发环境合并部署多个服务，仍需坚持独立 schema、独立契约和独立审计边界。
5. 平台 HTTP 服务的默认命名需要统一调整为 .Web、.Domain、.Infrastructure 主线；.Host 只保留给 Connector Host 宿主或后台进程类工程。

## Implementation Notes

1. 首批脚手架至少落下 PlatformGateway、Iam、FileStorage、AppHub、Ops 五个最小服务骨架，以及 common/Contracts、common/Sdk、common/Caching、common/Observability、common/Testing 五类窄共享库；Iam 骨架需要预留用户、角色、权限、外部客户端和授权授予的基础边界，FileStorage 骨架需要预留文件元数据、上传会话、上传指令、下载授权、Upload Provider、FilePurposePolicy、scanStatus 和对象存储适配边界。Notification 作为平台通用能力边界冻结，首批可以不阻塞注册纵切，但后续实现必须保持独立服务边界，不并入 Ops 或 Gateway。
2. Connector Host 与 Connector 需在 connector-hosts 根目录下建立独立 solution，并通过 Platform SDK 与版本化公开契约和主平台协作；主平台与 Connector Host 在同一主版本内不要求同步发布或同步小版本升级。
3. 平台 HTTP 服务的默认命名应采用点分 PascalCase，例如 Nerv.IIP.AppHub.Web、Nerv.IIP.AppHub.Domain、Nerv.IIP.AppHub.Infrastructure；Connector Host 宿主仍可保留 Nerv.IIP.ConnectorHost.Host 这类命名。
4. 服务内目录默认采用 src 与 tests；若未来出现额外脚手架约定，也必须在所有服务中统一，不得出现 src/tests 与 src/test 混用。
5. solution 模板中应明确每个服务的 Web、Domain、Infrastructure 项目命名规则，以及 Contracts 何时作为按需例外出现。
6. README、context map 与 repo layout 文档必须与本 ADR 保持一致。

## Out of Scope

1. 不在本 ADR 中定义每个服务的聚合根、命令查询列表与事件清单。
2. 不在本 ADR 中决定早期生产环境采用 3 个部署单元还是 5 个部署单元。
3. 不在本 ADR 中决定具体 ORM、消息消费者框架与迁移执行流程细节。
