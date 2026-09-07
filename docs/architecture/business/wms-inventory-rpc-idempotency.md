# WMS 与 Inventory RPC 幂等边界

本文描述 WMS 到 Inventory 的同步命令在“Inventory 已提交但调用方超时”场景下的当前幂等与恢复架构。长期决策见 [ADR 0019](../../adr/0019-wms-inventory-rpc-idempotency.md)；M2-L 清理前含测试实现说明的原文冻结于 [`../../reports/m2-l-wms-inventory-rpc-idempotency-pre-clean-2026-09-07.md`](../../reports/m2-l-wms-inventory-rpc-idempotency-pre-clean-2026-09-07.md)。

## 适用同步链路

1. WMS 创建拣货任务时请求 Inventory 库存预留。
2. WMS 创建盘点执行时请求 Inventory 创建盘点任务并冻结目标台账。

库存移动过账仍通过公开的 movement-requested 事件链路完成，不因为这两条同步 RPC 改成跨库写入。

## 幂等键所有权

WMS 为一个持久业务操作意图生成稳定幂等键，并在网络/进程重试时复用同一键。键必须从稳定 WMS 业务身份派生，而不是一次调用的临时 task/request id。

Inventory 把 idempotency key 与已提交业务事实绑定：

1. 相同键 + 相同载荷返回既有 reservation/count-task 结果；
2. 相同键 + 不同载荷返回稳定幂等冲突；
3. 不同键竞争同一业务唯一身份时返回业务冲突，不创建第二个冻结/预留；
4. 内部回退键使用独立命名空间，不能与显式调用方键在唯一索引上产生语义碰撞。

精确 key format 是代码契约，不在 Architecture 复制哈希字符串模板。

## 超时恢复

若 Inventory 已提交，而 WMS 在持久化返回的 Inventory public ID 前超时：

```text
WMS retry same business command
  -> recompute/reuse same idempotency key
  -> Inventory resolves committed fact by key
  -> returns same public id
  -> WMS persists reference
```

因此恢复动作是“以同一业务意图重放并对账”，不是生成新键、伪造下游 ID、跨库查询或创建补偿清理任务。

## 并发不变量

- 同键并发只能收敛到一份 Inventory 事实。
- 不同键但同一业务唯一身份不能绕过数据库唯一约束创建第二份事实。
- 失败命令在重试前必须清理/重建失效的 ORM tracking/unit-of-work 状态，不能复用一个已因唯一冲突进入不一致状态的上下文。
- WMS 只保存 Inventory 返回的公开 ID/业务引用，不取得 Inventory reservation/count-task 的事实所有权。
