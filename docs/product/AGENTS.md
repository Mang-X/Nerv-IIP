# Product 文档规则

本目录维护当前产品、角色、IA、UX 与产品验收语义。

- 修改前从 `docs/product/README.md` 选择对应产品域，并读取 `docs/architecture/document-language-governance.md`。
- 产品正文可以记录当前能力边界和明确后续产品缺口，但不得复制 GitHub/Linear 任务进度、CI run、一次性调查结果或历史状态总账。
- 当前实现事实以代码、公开契约和测试为准；当产品裁决与当前实现存在差距时，要明确区分“目标产品语义”和“当前实现事实”。
- 通用运行证据、环境启动、测试 lane 与证据 manifest 模板属于 Reference / Runbook / Governance，不要混回 Product。
- M2 迁移兼容页只导航，不追加 Product 正文；删除条件由 M2-M/M4 统一处理。
