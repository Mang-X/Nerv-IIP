# MES 线边收料来源分配

线边收料的 `MaterialLotId` 是 MES 线边追溯批次，不作为 Inventory 来源批次的精确过滤条件。MES 从 Inventory 的可用量明细读取实际库存批次，再按来源库位编码升序、批次号升序分配；总可用量不足时拒绝过账，并返回需求量与候选库位合计量。

`material_issue_requests.source_allocations_json` 持久化来源站点、库位、库存批次和数量。每个来源分配生成一条仓库出库 Inventory movement 明细；`pending_issue_leg_count` 与 `pending_issue_leg_posted_indexes_json` 用于异步回执和重试，只有所有来源出库明细与线边入库明细都成功后才推进 MES 收料状态。
