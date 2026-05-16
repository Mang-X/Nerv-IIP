# 部署基线

本文档承接 ADR 0008，定义 Nerv-IIP 的部署目标、工程落点与交付边界。它描述后续部署能力应如何演进，不代表当前第一、第二阶段纵切已经具备生产部署能力。

## 目标

1. 面向更多客户环境，而不是只支持单一容器部署。
2. 使用统一部署模型描述服务拓扑，避免 Aspire、Docker Compose、安装包和脚本各自漂移。
3. 保持服务边界和发布边界清晰，尤其是主平台与 Connector Host 的独立性。
4. 让本地开发、联调、PoC、私有化交付和受限环境安装拥有可追踪的入口。

## 部署目标矩阵

| 部署目标 | 主要场景 | 入口 | 边界要求 |
| --- | --- | --- | --- |
| Aspire | 本地开发、联调、Dashboard、服务发现、生成部署产物 | 平台级 AppHost | 只能有一个平台级编排入口，不为每个服务生成局部 AppHost。 |
| Docker Compose | PoC、小规模私有化、容器化单机部署、演示环境 | Aspire 生成的 Compose 或受控 overlay | 不把手写 compose 作为长期拓扑真相源。 |
| 安装包 | 无容器或传统运维环境 | Windows Service、systemd、zip/tar/deb/rpm 等制品 | 主平台服务与 Connector Host 分开分发。 |
| 整合安装脚本 | 实施交付、离线或弱联网部署、环境初始化 | PowerShell、Bash | 脚本负责检查、配置、初始化、注册服务、启动和诊断。 |

## 统一部署模型

1. 平台级 Aspire AppHost 是首选拓扑模型，负责描述服务、基础设施依赖、连接关系和观测资源。
2. netcorepal-web 模板创建的领域服务继续传入 `--UseAspire false`，表示不生成服务级 AppHost。
3. AppHost 不拥有领域规则，不替代 IAM、AppHub、Ops、File Storage 等服务边界。
4. Docker Compose 产物优先从 AppHost 生成；需要客户环境差异时，通过 overlay、参数、环境文件或安装脚本补充。
5. 安装包与整合安装脚本必须消费同一套配置口径，不能发明只在某个脚本中存在的隐式配置。

## 工程落点

```text
Nerv-IIP/
  infra/
    aspire/
      Nerv.IIP.AppHost/
    docker-compose.dev.yml
    compose/
    observability/
  scripts/
    install/
      windows/
      linux/
    verify-*.ps1
```

当前 `infra/docker-compose.dev.yml` 是本地依赖服务兜底入口。后续新增平台级 AppHost 后，开发联调和 Compose 生成应逐步迁移到 `infra/aspire/Nerv.IIP.AppHost`。

## Aspire AppHost 范围

首批 AppHost 应覆盖：

1. PlatformGateway。
2. Iam、FileStorage、AppHub、Ops。
3. Connector Host。
4. PostgreSQL、Redis、RabbitMQ、MinIO、Qdrant。
5. OpenTelemetry Collector，以及后续开源、自托管观测后端或暂不预置可视化后端的接入点。

后续 Notification、Knowledge、AI Integration 和 frontend console 进入可运行状态后，应纳入同一 AppHost，而不是新增第二套平台编排入口。

## Docker Compose 规则

1. `infra/docker-compose.dev.yml` 只作为早期本地依赖编排和兜底入口。
2. 面向完整平台的 Compose 文件应从 Aspire AppHost 生成。
3. 生成的 Compose 产物进入交付前必须校验 volumes、restart policy、资源限制、健康检查、镜像 tag、secret 注入和端口暴露。
4. 不在 Compose 文件中写入真实密钥、客户域名或不可公开的环境信息。
5. 若需要按客户环境定制，优先使用 overlay、`.env`、参数文件或安装脚本生成，不复制出长期维护的平行 Compose。

## 安装包规则

1. 主平台服务和 Connector Host 是不同分发单元。
2. Windows 包默认支持注册为 Windows Service。
3. Linux 包默认支持注册为 systemd service。
4. 安装包应包含可校验版本、配置样例、健康检查说明和卸载入口。
5. Connector Host 安装包不得引用主平台源码目录；只能依赖 Platform SDK、版本化契约包、公开 API 或等价发布制品。

## 整合安装脚本规则

Windows 脚本默认使用 PowerShell，Linux 脚本默认使用 Bash。脚本至少需要覆盖：

1. 运行时和依赖检查。
2. 端口、目录和权限检查。
3. 配置文件或环境文件生成。
4. 数据库初始化和迁移执行入口。
5. 服务注册、启动、停止和卸载。
6. 健康检查和诊断信息输出。

脚本必须设计为可重复执行；重复执行不应破坏已有配置或误删数据。

## Connector Host 部署

1. Connector Host 可以在主平台同机运行，也可以部署到受管节点。
2. Connector Host 必须支持独立安装、独立升级和独立回滚。
3. Docker Connector、Windows Service Connector、HTTP Connector 等适配能力只属于 Connector Host 侧，不进入主平台服务实现。
4. Connector Host 与主平台通过 Platform SDK、Connector Protocol、Ops Protocol、公开 HTTP API 和 IAM 授权关系协作。

## 当前阶段

1. 当前仓库已具备第一、第二阶段纵切验证脚本，但尚不是面向真实用户的可部署产品。
2. 当前 Compose 文件只覆盖本地依赖服务，还没有承载完整平台服务拓扑。
3. 下一阶段可先落地平台级 AppHost，再从 AppHost 生成 Compose，最后补安装包和整合安装脚本。

## 非目标

1. 不在本文档中冻结 Kubernetes、云厂商托管服务或 Helm Chart。
2. 不在本文档中定义客户现场的最终机器规格、网络 ACL、备份策略和密钥轮换。
3. 不要求当前阶段立即提供生产级安装包。
4. 不允许部署脚本绕过服务 API 直接写业务库来完成平台初始化。
