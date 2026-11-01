# AI BOM 导入说明

> AI 只负责从 XLSX/CSV 提取行；服务端负责确定性规范化、匹配和写入。

## 流程

1. 将表格每一行提取为 BOM DTO，**不要**让 AI 调用 `/components` 批量写入。
2. 使用本地 API 的 Bearer Token 调用 `POST /imports/bom/preview`。
3. 向用户展示每行的 `action`、`warnings`、`record` 和 `matchedComponentId`。
4. 只选择 `action` 为 `create` 或 `merge` 且 `warnings` 为空的行。
5. 用同一个 `previewId` 和选中的 `rowIndexes` 调用 `POST /imports/bom/commit`。

预览已过期/不存在、选中重复索引、或选择 `review` / 有警告的行都会被拒绝；应重新预览或由人工修正。

## AI 辅助提取

“批量导入”页面标题下提供 **复制 AI 提示词**。先复制该标准提示词，再将提示词和原始 BOM 交给 AI；提示词运行时只由程序提供，本文不重复全文，避免双源漂移。

AI 只能把可可靠确认的行放入 `rows`。无法确认型号/值、封装、分类或替代料的行必须放入 `excludedRows` 并说明原因，不能以“未分类”、`other/uncategorized` 或猜测字段占位。缺少型号/值或封装的行不可提交，`other/uncategorized` 也不是可接受的录入结果。

提示词不是安全边界：无论来源为何，Core/API 仍会在本地预览和提交时拒绝 `review`、有 warning、未分类、缺少型号/值或缺少封装的行。

## 预览请求

```json
{
  "rows": [
    {
      "quantity": 2,
      "designators": ["C3", "C4"],
      "value": "100nf",
      "footprint": "C0603",
      "manufacturerPart": "",
      "manufacturer": "",
      "supplierPart": ""
    }
  ]
}
```

被规范化为：

```json
{
  "name": "100nF",
  "partNumber": "100nF",
  "category": "贴片电容(MLCC)",
  "majorCategoryId": "passives",
  "subCategoryId": "mlcc",
  "package": "0603",
  "attributes": { "footprint": "C0603" }
}
```

**强制规则：型号/值和封装必须分开。** 绝不生成 `100nf_C0603`、`10K_R0805` 之类的 `name` 或 `partNumber`。

BOM 行中的 `quantity` 与 `designators` 会随元件记录一起写入 SQLite 与 `catalog.json` 快照，在 Windows 与 Android 端用于展示 BOM 数量与位号；它们不会与型号/封装拼接。重复行合并（merge）时位号取并集、数量取本次有效值。

## 固定分类与人工复核

`majorCategoryId` 和 `subCategoryId` 是固定、版本化的目录契约；`category` 仅是由子类 ID 推导出的兼容显示标签，不能创建任意自由文本分类。位号、已知 MPN 和受支持的旧分类可确定地映射为稳定 ID（例如 `C1` -> `passives/mlcc`，`AMS1117` -> `protection-power/ldo`）。

没有位号、已知 MPN 或受支持分类证据的行会返回 `other/uncategorized` 和 warning，并且 action 为 `review`；这类行不可提交。先补全型号、封装或分类后重新预览。

## MPN 示例

```json
{
  "quantity": 1,
  "designators": ["C1"],
  "value": "10uF",
  "footprint": "C1206",
  "manufacturerPart": "CL31A106KAHNNNE"
}
```

结果使用原样 MPN：`name` 和 `partNumber` 均为 `CL31A106KAHNNNE`，`package` 为 `1206`，原始值与 footprint 仅保留为证据属性。

## 提交请求

```json
{
  "previewId": "来自预览响应的 id",
  "rowIndexes": [0, 3]
}
```

提交以一个事务保存选中行并导出一次快照。不要重试带有 `review` 或警告的行；先人工核实型号、封装和替代料。
