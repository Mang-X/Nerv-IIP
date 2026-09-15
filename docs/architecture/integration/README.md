# Integration Architecture

本目录承载跨进程、跨演进单元的 Current Architecture 契约与接入边界。

- [`api-contracts.md`](api-contracts.md)：Gateway/OpenAPI/生成客户端的运行时契约链。
- [`connector-host-machine-auth.md`](connector-host-machine-auth.md)：Connector Host 机器身份与授权边界。
- [`connector-protocol-v1.md`](connector-protocol-v1.md)：Connector Host 与平台协议 V1。

API 规范性规则、命令和人工 Reference 分别由 Governance、Runbook 与 Reference 维护，避免在 Architecture 复制。