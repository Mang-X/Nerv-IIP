# NERV-669 真 PostgreSQL 分片缺口收口设计

## 目标与边界

本设计只收口 NERV-669 走查发现并由 NERV-812 / #1499 跟踪的一个缺口：`InventoryDirectoryPostgresTests` 仍在 `business-core-a` 快速分片中启动真实 PostgreSQL 容器，且现有门禁只校验“已排除项必须有政策登记”，无法发现“真实依赖测试根本没有被排除”。

本轮不接入 `real-postgres`、`full-chain`、`performance` 三条 GitHub-hosted heavy lane。三条 owner script 的依赖准备、TRX 输出和 zero-execution（零执行）契约不同，必须分别设计和交付；required summary 仍归 NERV-668，统一 scenario runner（场景执行器）仍归 NERV-673。本轮不改业务 HTTP endpoint、OpenAPI、数据库 schema、migration 或前端。

## 方案比较与裁决

### 方案 A：只把整个测试类加入 `excludedTestClasses`

改动最小，但会让该类第一个测试在设置 `NERV_IIP_TEST_POSTGRES` 时提前返回，形成“通过但没有验证 Docker 自建 PostgreSQL”的假绿；同时无法阻止未来新增的真实依赖测试再次漏出快速分片。拒绝。

### 方案 B：显式两种运行模式，并增加反向门禁（采用）

把两个 `[Fact]` 作为独立政策身份、以一个类级选择器交给 `real-postgres` owner lane：一个验证 Docker 自建 PostgreSQL，另一个验证外部 PostgreSQL。测试夹具显式选择运行模式，避免依赖环境变量的隐式反向 gate；因此该类在 owner lane 设置外部连接串时，Docker 测试也不会提前返回或改走外部连接。分片治理扫描受审计的真实依赖原语，并要求命中的测试类型已从快速分片整体排除；契约测试通过可运行 fixture 与削弱变异证明该门禁会红。

该方案保留 fast shard 的零 skip 口径，同时让两种真实依赖行为都拥有精确、可验证的执行身份。反向门禁先覆盖仓库当前已确认的直接 Docker 进程入口；后续若引入 Testcontainers 或其他 broker 原语，应显式扩展扫描规则和变异夹具，不在本 PR 猜测未来形态。

### 方案 C：本 PR 同时把三条 heavy lane 接进 CI

会同时改 workflow、三套运行器、证据采集、超时预算和 required 关系，且 full-chain owner script 当前并不运行 manifest 中的 `FullChain.Tests` 项目。该范围无法作为一个独立可审 PR 交付，拒绝。

## 组件与数据流

1. `InventoryDirectoryPostgresTests` 为两个测试显式选择 `self-hosted-docker` 与 `external-postgres` 模式。Docker 模式忽略外部连接串并创建/清理自己的容器；外部模式要求 `NERV_IIP_TEST_POSTGRES`。两者共享一个环境门禁属性，使默认快速命令不会把真实依赖记为 skip，因为该类会被整体排除。
2. `test-evidence-policy.json` 为两个方法登记稳定身份、真实依赖原因和 `requiredLane: postgres`。
3. `backend-test-shards.json` 在 `business-core-a.excludedTestClasses` 中登记类选择器；`excludedTestLanes` 继续由现有门禁从政策推导为 `real-postgres`。
4. `verify-backend-test-shards.ps1` 扫描受审计的直接 Docker 进程入口，解析所在测试类型，并验证该类型已由某个快速分片的类级排除选择器覆盖。发现未登记入口时输出文件和类型并以非零退出。
5. `backend-test-shards.Tests.ps1` 构造临时测试项目/源码运行真实门禁：未排除时先红，补上类选择器后变绿；另验证生产 manifest 精确拥有新选择器，避免用源码字面量断言冒充行为证明。

## 错误处理与安全

- Docker 或外部 PostgreSQL 不可用时，owner lane 失败并保留脱敏诊断，不降级为 fast lane skip。
- 反向扫描不能静默忽略无法解析的受审计测试文件；解析失败本身是治理错误。
- 不记录连接串、容器凭据或测试 body；诊断只包含仓库相对路径、测试类型和方法身份。
- 所有标识符比较继续使用 ordinal（序数）语义，不能退回 PowerShell 默认 culture-aware（区域性）比较。

## 验证

- TDD 红灯：在生产 manifest 尚未排除该类时，新增反向门禁契约必须失败；测试运行模式夹具在旧实现上必须暴露 Docker 模式被外部环境变量短路。
- 绿灯：`backend-test-shards.Tests.ps1`、`verify-backend-test-shards.ps1`、`test-evidence.Tests.ps1`、脚本治理及 Inventory 定向测试通过。
- 削弱变异：删除类选择器、删除任一政策身份或把直接 Docker 测试重新放回快速分片时，门禁必须失败。
- 最终范围检查：`git diff --check`，并确认无 endpoint、OpenAPI、schema、migration 或前端 diff。
