# 随手查料 · Android 查询端

Windows 端“电子元件入库台”的配套只读查询应用，技术栈为 Capacitor 8 + React 19 + TypeScript + Vite。

## 职责与边界

- 只读：浏览、搜索、筛选、查看元件详情，不修改正式目录。
- 数据：Windows 端 SQLite 是唯一写源。手机通过局域网 UDP 发现 Windows 端，拉取 `/mobile/snapshot` 快照并保存在 localStorage（键 `catalog.snapshot`）。
- 更新：仅在目录同步成功后检查 `/mobile/app-update`，下载 `/mobile/app.apk` 并校验字节数与 SHA-256，然后交给 Android 系统安装器，不静默安装。
- 安全：局域网链路使用明文 HTTP，仅限可信私有网络；完整说明见 [发布包/Android同步说明.md](../发布包/Android同步说明.md)。

## 环境

- Node.js 24 / npm 11
- JDK 21 与 Android SDK 36（仅原生构建需要）
- 仓库已包含 `node_modules` 时无需重新安装

## 常用命令（在 `Android/` 下）

```powershell
npm test               # vitest 单元测试
npm run build          # tsc -b && vite build
npm run lint           # oxlint
npm run version:check  # 与仓库根 version.json 校验版本
npm run android:sync   # npx cap sync android
cd android
.\gradlew.bat assembleDebug --no-daemon
```

## 版本

产品 `1.1` / Android `versionCode 4` / taxonomy `v1`，由仓库根 `version.json` 单一真源生成（`scripts/version.mjs`），不要手工改动 `src/version.generated.ts`。

## 原生层

- `CatalogDiscoveryPlugin.java`：UDP 广播发现 Windows 端（端口 10850）。
- `AppUpdatePlugin.java`：下载、长度/SHA-256 校验并通过 FileProvider 交给系统安装器。
