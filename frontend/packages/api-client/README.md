# API client

应用从 `@nerv-iip/api-client` 稳定入口消费 Gateway SDK 与类型；生成目录由 OpenAPI producer 更新，不作为应用的深层导入入口。生成与漂移验证见 [API codegen Runbook](../../../docs/runbooks/api-codegen.md)。

Scheduling 首版创建、详情和 revision 的 `candidate` 共用方案类型。方案的 `validationContext` 是 Scheduling 从冻结快照投影的只读上下文；消费方保留资源、工序前序、交期与固定占用原值，不能从当前页面补造事实。旧方案可能没有上下文。

```ts
import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingValidationContext,
} from '@nerv-iip/api-client'

function readValidationContext(
  plan: BusinessConsoleSchedulePlan,
): BusinessConsoleSchedulingValidationContext | null | undefined {
  return plan.validationContext
}
```

此包提供契约与传输能力；浏览器校验规则和反馈 UI 由应用消费方负责。
