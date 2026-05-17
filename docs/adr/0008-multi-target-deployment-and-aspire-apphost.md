# ADR 0008: 多部署目标与统一 Aspire AppHost 策略

- Status: Accepted
- Date: 2026-05-16

## Context

Nerv-IIP 面向私有化、混合部署和工程联调场景。目标环境可能包含开发者本机、小规模 PoC、客户内网服务器、Windows-only 环境、Linux 服务器、具备容器运行时的环境，以及不能使用容器的受限环境。

当前仓库已有 `infra/docker-compose.dev.yml`，但它只承载本地依赖服务的开发编排，不应被理解为最终部署方式已锁定为手写 Docker Compose。与此同时，netcorepal-cloud-framework 与 .NET Aspire 生态具备天然协同空间，Aspire 也可以作为 Docker Compose、Dashboard、服务发现和多资源编排的统一入口。

如果各服务分别通过模板生成自己的 Aspire AppHost，平台会快速产生多套局部编排入口，导致环境变量、依赖服务、端口、观测和发布流程漂移。因此需要冻结一个平台级部署策略：支持多种交付形态，但不让每种形态各自定义一套拓扑真相。

## Decision

1. Nerv-IIP 采用“多部署目标，单一部署模型”的策略。
2. 平台级 Aspire AppHost 是分布式拓扑、开发联调、Dashboard、服务发现和 Compose 生成的首选模型入口。
3. 平台领域服务继续通过 netcorepal-web 模板显式传入 `--UseAspire false`，含义是不生成服务级局部 AppHost；统一编排入口由平台级 AppHost 承担。
4. Docker Compose 是轻量私有化、PoC、小规模单机部署和容器化交付的目标形态；优先由 Aspire AppHost 生成，再按交付需要补充 volumes、restart policy、资源限制、镜像 tag、secrets 和外部网络配置。
5. 安装包是不能或不希望使用容器的环境的一等部署形态；Windows 默认面向 Windows Service，Linux 默认面向 systemd。
6. 整合安装脚本是实施交付入口；Windows 使用 PowerShell，Linux 使用 Bash，负责环境检查、配置生成、依赖检查、数据库初始化、服务注册、启动和诊断输出。
7. Connector Host 必须保持可独立安装和独立升级；它既可以被纳入 Aspire/Compose 联调，也可以作为 Windows Service、systemd service 或压缩包部署在受管节点上。
8. 物理部署单元可以按环境合并或拆分，但 IAM、File Storage、AppHub、Ops、Notification、Knowledge、AI Integration、Gateway 和 Connector Host 的逻辑边界不得因部署形态变化而回退。
9. `infra` 目录拥有部署模型、Compose 产物、基础设施模板和观测栈配置；`scripts` 目录拥有安装、验证、发布辅助和环境诊断脚本。
10. 脚本的可信执行、分类、副作用声明、超时、进程清理和诊断门禁按 ADR 0010 执行；本 ADR 只定义脚本在部署目标中的职责。

## Rationale

1. Aspire 适合表达平台服务、基础设施依赖、Connector Host 和观测资源之间的关系，能减少手写多服务编排的漂移。
2. Docker Compose 仍是很多私有化和 PoC 环境最容易落地的容器入口，但完整 Compose 应从统一模型派生，而不是长期手写多份。
3. 安装包和整合安装脚本覆盖无容器、弱联网、Windows 服务器和传统运维场景，能降低客户环境适配风险。
4. Connector Host 的安装位置天然更分散，必须独立于主平台发布包和主平台服务运行方式。
5. 统一部署模型让开发、测试、演示和交付共享同一套拓扑语义，有利于持续验证服务边界和配置边界。

## Consequences

1. `infra` 需要新增平台级 Aspire AppHost，并逐步把当前本地依赖编排、服务启动、Dashboard 和 Compose 生成收敛到它。
2. `infra/docker-compose.dev.yml` 可以继续作为早期本地依赖兜底入口，但不再代表最终部署拓扑的唯一来源。
3. 交付脚本需要具备幂等性和诊断输出，否则安装包形态会很快变成不可维护的手工流程。
4. Compose 生成结果仍需要交付层校验，特别是持久化卷、重启策略、资源限制、secret 注入、镜像来源和客户网络环境。
5. 多部署目标会增加验证矩阵；每个发布版本至少需要验证 Aspire 联调、Compose 部署、Windows 安装脚本和 Linux 安装脚本中的核心路径。
6. 服务代码不得读取某一种部署形态特有的隐式事实；配置必须通过统一配置项、环境变量、密钥来源或连接字符串进入运行时。
7. 安装、验证和发布脚本需要纳入脚本治理门禁，否则多部署目标会在脚本层重新产生不可诊断的漂移。

## Implementation Notes

1. 平台级 AppHost 建议落点为 `infra/aspire/Nerv.IIP.AppHost`，并显式纳管 Gateway、Iam、FileStorage、AppHub、Ops、Connector Host 和基础设施资源。
2. Aspire AppHost 首批至少覆盖 PostgreSQL、Redis、RabbitMQ、MinIO、Qdrant、OpenTelemetry Collector 和平台服务之间的引用关系。
3. Compose 发布产物应作为构建或发布结果输出，不应手写覆盖生成产物；确需人工补充的生产配置应以 overlay、模板或发布脚本方式表达。
4. 安装包需要把主平台服务与 Connector Host 作为不同分发单元处理；Connector Host 安装包不得依赖主平台源码目录。
5. Windows 整合安装脚本建议负责 .NET runtime/SDK 检查、配置目录创建、服务注册、端口检查、连接串写入和健康检查。
6. Linux 整合安装脚本建议负责 runtime 检查、目录创建、systemd unit 写入、权限设置、环境文件生成、服务启动和健康检查。
7. 后续如引入 Kubernetes，应作为新的部署目标接入同一部署模型，不替代当前 Aspire、Compose、安装包和整合脚本策略。
8. 脚本实现细则、helper 契约和迁移清单见 `docs/architecture/script-automation-governance.md`。

## 2026-05-17 Amendment

第四阶段已按本 ADR 落地平台级 AppHost，位置为 `infra/aspire/Nerv.IIP.AppHost`。当前首批覆盖范围是 AppHub、Ops、PlatformGateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ。

Iam、FileStorage、Notification、Knowledge、AI Integration、MinIO、Qdrant、OpenTelemetry Collector 和 frontend console 仍属于完整 AppHost 后续范围。当前覆盖范围不改变本 ADR 的最终目标，只说明第四阶段已经完成第一批真实基础设施拓扑，而不是完整交付拓扑。

## Out of Scope

1. 本 ADR 不冻结最终 CI/CD 平台、镜像仓库、制品仓库或签名机制。
2. 本 ADR 不定义生产环境的具体机器规格、网络拓扑、备份周期、密钥轮换和灾备策略。
3. 本 ADR 不决定是否必须支持 Kubernetes；Kubernetes 可作为后续部署目标单独评估。
4. 本 ADR 不要求当前第一、第二阶段纵切立即完成安装包和脚本实现，只冻结后续演进方向。
