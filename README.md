# 电子元件入库与检索系统

一个面向电子元件目录管理的双端项目：

- `ComponentCatalogWinUI/`
  Windows 主维护端，负责录入、编辑、删除、导入导出与本地 API。
- `Android/`
  Android 查询端，负责离线搜索、筛选、详情查看与快照导入。
- `ComponentCatalogCore/`
  .NET 共享核心，包含仓储、导入导出、搜索与 API 服务。
- `shared/`
  前端共享类型与搜索规则实现。

## 当前目录结构

- `ComponentCatalog.slnx`
  .NET 解决方案入口。
- `docs/`
  设计与实施文档。

## 常用命令

### Android

```powershell
cd Android
npm test
npm run build
npm run lint
npm run android:sync
cd android
.\gradlew.bat assembleDebug --no-daemon
```

### .NET Core / WinUI

```powershell
dotnet test ComponentCatalogCore.Tests
dotnet build ComponentCatalog.slnx
```

## Git 约定

- 已忽略构建产物、`node_modules`、`bin/obj`、Android 调试截图与本地日志。
- 建议发布前至少通过：
  - `Android: npm test && npm run build && npm run lint`
  - `.NET: dotnet test ComponentCatalogCore.Tests`
