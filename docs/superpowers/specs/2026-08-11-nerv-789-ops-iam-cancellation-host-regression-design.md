# NERV-789 Ops IAM 取消宿主回归设计

## 背景与现状

NERV-789 最初记录的代码快照已过期。当前 `main` 已在 PR #1468 中完成以下生产语义：

- 调用方令牌已取消时，`OperationCanceledException` 原样传播且不记录 `iam-unavailable`；
- 调用方令牌未取消时，裸 `OperationCanceledException` 与 `TaskCanceledException` 都归为 helper 自有超时，记录 `request-timeout` 并失败关闭为 `iam-unavailable`；
- 连接预算与请求预算显式分开，测试可用毫秒级配置覆盖。

PR #1526 又把原宿主测试的不可达端口换成进程内 IAM handler，并补回 Production 宿主级 401 断言。因此不应再次修改生产 catch，也不应恢复依赖网络栈时序的死端口测试。

仍缺少的一条直接证据是：在真实 Production 测试宿主中，当 IAM 类型化客户端抛出调用方未取消的**裸** `OperationCanceledException` 时，完整 endpoint 管线仍返回 401，而不是让异常逃逸。

## 方案比较

1. 直接关闭 NERV-789：代码事实已经满足主要语义，但没有补齐票面要求的宿主层回证，也无法形成独立 PR。
2. 补宿主层回归测试：复用 #1526 的进程内 IAM handler，让它可脚本化返回响应或抛异常；新增 Production endpoint 用例覆盖裸取消。改动最小且能关闭剩余证据缺口，采用此方案。
3. 再次修改生产 catch：当前实现已经是正确的 `OperationCanceledException` 两段式分类，重复修改没有行为收益，拒绝。

## 设计

将 `StubbedIamCredentialHandlerFilter` 从“固定返回一个状态码”扩展为“执行一个进程内脚本”，同时保留现有状态码构造方式，避免改变 #1526 的既有测试表达。

新增宿主级测试时：

1. 创建会返回 faulted task、异常为裸 `OperationCanceledException` 的 IAM handler；
2. 启动与现有用例相同的 Production Ops 宿主，继续保留真实环境、持久化治理、内部服务认证和连接器认证管线，只隔离 CAP 后台处理与 IAM 出网；
3. 使用未取消的 endpoint 请求调用 pending endpoint；
4. 断言响应为 401，并断言 IAM 恰好被调用一次，排除内部服务认证或请求头提前短路导致的同码假绿。

生产代码、业务 endpoint、API 契约、数据库、Gateway、前端与共享测试基建均不修改。

## 判别力与验收

回归测试必须通过一次临时变异证明判别力：把生产侧第二个 `catch (OperationCanceledException)` 暂时削弱为 `catch (TaskCanceledException)`，新宿主测试必须失败；随后撤销变异，测试恢复通过。变异只用于本地证据，不进入提交。

最终验收包括：

- 新用例 targeted 通过；
- 原 Production 401 用例与 caller/helper 取消分类单测通过；
- 新用例在四路并发负载下多轮通过且没有 skip；
- `Nerv.IIP.Ops.Web.Tests` 全程序集通过；
- 后端确定性 checker 与 `git diff --check` 通过；
- 无 endpoint、OpenAPI、facade 或产品文档影响。
