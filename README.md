# 电子元件入库与检索系统

一个面向电子元件目录管理的双端系统：**Windows 主维护端**负责录入、编辑、导入与数据管理，**Android 查询端**负责离线搜索、筛选、详情查看与局域网同步。

> 产品版本 **1.1** · Android `versionCode 4` · 分类体系 `v1`

## 功能特性

### Windows 主维护端（WinUI 3）

- 元件录入、编辑、删除，按大类/子类/封装/持有状态/属性筛选与搜索
- CSV 批量导入，支持跳过、覆盖、合并别名策略
- AI-BOM 导入：预览 → 挑选 → 提交，拒绝未分类、缺型号/封装或带警告的行
- SQLite 作为唯一写入真源，`catalog.json` 作为只读快照导出
- 备份与恢复：SHA-256 校验、完整性检查、恢复前安全备份、失败自动回滚
- 本机管理 API（仅 `127.0.0.1`，Bearer 鉴权）
- 局域网手机同步：UDP 自动发现 + 只读快照服务
- 端口可配置、单实例保护、日志轮转、诊断导出
- Android 安装包发布：签名、zipalign、SHA-256 与证书指纹校验

### Android 查询端（Capacitor + React）

- 离线目录浏览，关键词搜索，大类/子类/封装/持有/属性筛选
- 匹配度 / 名称 / 型号 / 分类排序
- 元件详情、BOM 数量与位号展示
- UDP 自动发现 Windows，拉取并缓存目录快照
- 深色主题、Material 3 动效、无障碍支持
- 应用内更新：下载到缓存、校验大小与 SHA-256 后交给系统安装器

## 架构概览

```text
┌──────────────────────── Windows ────────────────────────┐
│  WinUI 3 页面                                           │
│      │                                                  │
│  CatalogWorkspaceService                                │
│      ├── CatalogRepository ── SQLite（catalog.db）      │
│      ├── catalog.json（只读快照，原子替换）             │
│      ├── CatalogApiServer  127.0.0.1:10849（Bearer）   │
│      └── MobileDiscoveryServer  UDP:10850（只读发现）   │
└──────────────────────────┬──────────────────────────────┘
                           │ 同一可信私有 Wi‑Fi
┌──────────────────────────▼──────────────────────────────┐
│  Android（Capacitor + React）                            │
│  localStorage 快照  ←  GET /mobile/snapshot             │
│  AppUpdatePlugin     →  系统安装器（不静默安装）        │
└─────────────────────────────────────────────────────────┘
```

## 技术栈

| 模块 | 技术 |
|---|---|
| 共享核心 | .NET 10、Microsoft.Data.Sqlite、System.Text.Json |
| Windows 端 | WinUI 3、Windows App SDK 2.x、xUnit |
| Android 端 | Capacitor 8、React 19、TypeScript 6、Vite 8、Vitest |
| 脚本 | Node.js ESM、PowerShell |
| 构建与发布 | MSBuild、Gradle、zipalign、apksigner、aapt |

## 目录结构

```text
ComponentCatalogCore/       .NET 共享核心：契约、仓储、搜索、导入、API、备份、分类
ComponentCatalogCore.Tests/ xUnit 测试
ComponentCatalogWinUI/      WinUI 3 桌面主维护端
Android/                    Android 查询端（React Web + Capacitor 原生壳）
shared/                     前端共享类型与搜索规则
scripts/                    版本同步、Android 发布、一致性校验
发布包/                     面向交付的说明、启动脚本与 Android 更新清单
docs/                       文档索引、当前状态、验证记录与历史归档
ComponentCatalog.slnx       .NET 解决方案入口
version.json                版本单一真源
```

## 快速开始

### 环境要求

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Node.js 24 / npm 11（Android Web 与发布脚本）
- 原生 Android 构建：JDK 21+、Android SDK 36

### .NET（核心与 WinUI）

```powershell
dotnet restore ComponentCatalog.slnx
dotnet build ComponentCatalog.slnx -c Release
dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj
```

仓库内自带 `.dotnet-sdk/` 时可直接使用：

```powershell
.\.dotnet-sdk\dotnet.exe build ComponentCatalog.slnx
.\.dotnet-sdk\dotnet.exe test ComponentCatalogCore.Tests
```

### Android Web 端

```powershell
cd Android
npm install
npm test
npm run build
npm run lint
```

### Android 原生包

```powershell
cd Android
npm run android:sync
cd android
.\gradlew.bat assembleRelease --no-daemon
```

正式发布使用仓库根目录的 `发布包/更新安卓安装包到发布包.bat`；需要外部签名材料，详见 [发布包/Android同步说明.md](发布包/Android同步说明.md)。

## 数据与版本

- 运行数据默认位于 `ComponentCatalogData/`：`Data/`（SQLite 与快照）、`Logs/`、`Backups/`；该目录已被 `.gitignore` 排除，**不要提交真实目录数据**。
- 可用环境变量 `COMPONENT_CATALOG_ROOT` 覆盖数据根目录。
- 版本单一真源为 `version.json`（当前 `1.1` / Android code `4` / taxonomy `1`）：

```powershell
node scripts/version.mjs --check   # 校验生成文件与版本一致
node scripts/version.mjs --write   # 同步 C#/TS/props 与发布清单
```

## 本机 API（可选）

默认仅监听 `127.0.0.1:10849`，管理接口需要 Bearer Token；Android 同步使用局域网只读端点。

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/components`、`/components/search` | 查询/搜索元件 |
| `GET/POST/PUT/DELETE` | `/components/{id}`、`/components` | 元件管理（Bearer） |
| `GET` | `/categories`、`/taxonomy/categories` | 分类数据 |
| `POST` | `/imports/bom/preview`、`/imports/bom/commit` | AI-BOM 预览/提交（Bearer） |
| `GET` | `/mobile/snapshot` | Android 只读快照（仅局域网） |
| `GET` | `/mobile/app-update`、`/mobile/app.apk` | Android 更新清单/安装包（仅局域网） |

## 测试与校验

| 范围 | 命令 | 当前状态 |
|---|---|---|
| .NET 核心（不含 API 宿主） | `dotnet test ComponentCatalogCore.Tests --filter "FullyQualifiedName!~CatalogApiServerTests"` | 104/104 |
| Android Web | `npm --prefix Android test` | 62/62 |
| Android 构建 | `npm --prefix Android run build` | PASS |
| 版本一致性 | `node scripts/version.mjs --check` | PASS |
| 脚本测试 | `node --test scripts/` | 17/17 |

> 13 个 `CatalogApiServerTests` 在当前开发机受 .NET 10 `HttpListener` 与 Windows 11 兼容问题阻塞；详见 [docs/verification/2026-11-01-verification.md](docs/verification/2026-11-01-verification.md)。

## 常见问题

**双击 `发布包/启动Windows程序.bat` 后提示 `Access to the path 'C:\Users\<用户>\.dotnet' is denied`**

启动脚本现在会**始终**使用仓库内的构建缓存（`.dotnet-home/`、`.nuget-packages/`、`.nuget-http-cache/`、`.msbuild-temp/`，均已被 `.gitignore` 忽略），不再依赖用户目录下的 .NET/NuGet/MSBuild 状态；构建失败时若已有可执行文件，会提示并使用现有版本启动。

**启动脚本现场编译较慢**

`发布包/` 中没有已发布的 exe 时会先执行 `dotnet build -c Release`。如需免编译分发，可先
`dotnet publish` 生成自包含版本，或使用 [发布包/Verify-WindowsPublish.ps1](发布包/Verify-WindowsPublish.ps1) 的发布流程。

**双击 `发布包/启动Windows程序.bat` 每次提示“打开文件 - 安全警告 / 无法验证发布者”**

这是 Windows 对来自网络的 `.bat` 添加的 Mark-of-the-Web 标记，不是程序签名问题。下载仓库压缩包后请先解压，再在解压目录执行一次：

```powershell
Get-ChildItem "D:\b1497\Downloads\入库" -Recurse | Unblock-File
```

或右键该 bat → 属性 → 勾选“解除锁定”→ 确定。仓库内文件已全部解除，正常双击不会再弹；请勿直接在压缩包预览窗口里双击运行。

**启动后提示“初始化失败：无法启动本地 API … 句柄无效”**

这是当前 Windows 版本与 .NET 10 `HttpListener` 的兼容问题（错误 6，HTTP.sys 会话初始化失败），不是端口被占用。桌面端现在会继续以离线模式打开，目录浏览、搜索、编辑、导入与备份均可用；只有本机 API 与手机同步不可用。若需要手机同步，可等待后续改用不依赖 `HttpListener` 的传输实现。

## 文档

- [docs/README.md](docs/README.md)：文档索引
- [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)：当前状态、验证结果、已知限制与 backlog
- [docs/verification/2026-11-01-verification.md](docs/verification/2026-11-01-verification.md)：完整验证记录
- [发布包/AI-BOM导入说明.md](发布包/AI-BOM导入说明.md)：AI-BOM 导入流程
- [发布包/Android同步说明.md](发布包/Android同步说明.md)：局域网同步与 Android 更新
- [发布包/分类体系说明.md](发布包/分类体系说明.md)：两级固定分类体系

## 安全与隐私

- `ComponentCatalogData/`、`api.token`、`lan-settings.json`、日志、备份与 APK 均不纳入版本控制。
- 本机 API 默认只绑定回环地址；局域网同步仅面向可信私有网络，当前使用 HTTP，请勿在公共网络使用。
- 发布签名材料（keystore / properties）必须放在仓库外，禁止提交。
- Android 更新会校验 APK 大小与 SHA-256，并通过系统安装器确认，不静默安装。
