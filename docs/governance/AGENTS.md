# Governance 文档规则

本目录维护当前必须遵守的工程规则。

- 修改前从 `docs/governance/README.md` 选择直接相关主题，并按 `docs/governance/docs/language.md` 处理人工文本。
- Governance 只写现态约束、适用范围、例外原则和 producer；不得追加 Issue 状态、负责人、CI run、事故过程、阶段完成史或时点计数。
- 精确版本、权限码、Schema、路由、seed、provider 和运行行为以代码、配置、生成物、测试与帮助输出为准；Governance 只规定约束，不维护平行实现账本。
- 历史调查和修复证据进入 `docs/reports/`；长期取舍按 ADR 规则处理。
- 不为迁移分类新增永久 manifest、自然语言 checker、同步脚本或独立 CI step。既有机器契约需要迁移时只改权威路径，不借机放宽或增强规则。
- M4 后旧 `docs/architecture/*` 平铺兼容页已退出；当前 Governance 只链接 canonical owner，历史旧路径通过 Reports/Git 追溯，不重新建立兼容页。
