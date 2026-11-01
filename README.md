# 电子元件入库与检索系统

一个面向电子元件目录管理的双端项目：

- `ComponentCatalogWinUI/`
  Windows 主维护端：录入、编辑、删除、CSV/AI-BOM 导入、本地 API、备份恢复、局域网同步与 Android 安装包发布。
- `Android/`
  Android 查询端：离线搜索、筛选、详情查看、快照同步与应用内更新。
- `ComponentCatalogCore/`
  .NET 共享核心：SQLite 仓储、搜索、导入导出、API/发现服务、备份与校验。
- `shared/`
  前端共享类型与搜索规则实现（与 .NET 打分语义对齐）。
- `scripts/`
  版本同步、Android 发布、发布/一致性校验脚本。
- `docs/`
  设计与测试文档；当前验证状态见 [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)。

## 数据与版本

- 运行数据默认位于 `ComponentCatalogData/`（`Data/` 数据库与快照、`Logs/`、`Backups/`），已被 `.gitignore` 排除，不要提交真实目录数据。
- 可用环境变量 `COMPONENT_CATALOG_ROOT` 覆盖数据根目录。
- 版本单一真源为 `version.json`（当前产品 `1.1`、Android `code 4`、taxonomy `1`）；
  `node scripts/version.mjs --write` 会同步 C#/TS/props 生成文件与发布清单，`--check` 做一致性校验。

## 常用命令

### Android

```powershell
cd Android
npm test
npm run build
npm run lint
npm run version:check
npm run android:sync
cd android
.\gradlew.bat assembleDebug --no-daemon
```

### .NET Core / WinUI

仓库根目录的 `.dotnet-sdk/` 提供固定 SDK 时可优先使用 `.\.dotnet-sdk\dotnet.exe`：

```powershell
dotnet test ComponentCatalogCore.Tests
dotnet build ComponentCatalog.slnx
```

### 版本与脚本校验

```powershell
node scripts/version.mjs --check
node --test scripts/
```

## Git 约定

- 已忽略构建产物、`node_modules`、`bin/obj`、Android 调试截图与本地日志。
- 建议发布前至少通过：
  - `Android: npm test && npm run build && npm run lint`
  - `.NET: dotnet test ComponentCatalogCore.Tests`
  - `node scripts/version.mjs --check`
