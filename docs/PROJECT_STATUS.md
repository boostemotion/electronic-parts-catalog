# 项目状态（2026-11-01）

本文件记录本轮“验证基线 + 分组提交 + 文档校正 + 功能补齐”后的状态快照，供后续开发与排查参考。

## Git 基线

- 分支 `main`，仓库无 remote，未 push。
- 本轮整理出以下本地提交：

| 提交 | 说明 |
|---|---|
| `8db5fb5` | chore: add lightweight release helper scripts（既有基线的最终提交） |
| `7539a4a` | chore(version): 建立版本单一真源与生成脚本 |
| `a905022` | feat(core): 两级分类、BOM 导入与共享契约 |
| `c11dc16` | feat(core): 备份恢复、存储迁移、诊断与局域网传输 |
| `330c627` | feat(winui): 接入分类、BOM 提示、备份与局域网设置 |
| `402b972` | feat(android): 快照同步、局域网发现与应用内更新 |
| `b0981de` | chore(release,docs): 发布包脚本、校验工具与测试证据 |
| `071c579` | fix(winui): 批量导入复用工作区仓储，消除双连接与快照竞争 |
| `f92cff7` | fix(shared): 统一 package 筛选的大小写语义 |
| `15df6c7` | chore(deps): 升级 Microsoft.Data.Sqlite 至 10.0.14 并消除 NU1903 |
| `a80432c` | chore(scripts): Android 工具链路径可配置化（SDK/JDK/暂存目录） |
| `7ffcdea` | feat(core): 日志按大小轮转，避免无界增长 |
| `2001b91` | feat(winui): 单实例保护与删除二次确认 |
| `332e27a` | feat(core): BOM 数量与位号落库并进入快照与 Android 展示 |
| `0718e41` | feat(winui): 支持配置本机 API 与 UDP 发现端口 |
| `96995e9` | test(core): 覆盖快照导出失败后的重启恢复 |

- 整理前的完整工作区 checkpoint 为 `c4975b9`（不在 main 历史中，仍可通过 reflog 找回）。
- 上表为基线（C1–C6）、修复（C7–C8）、依赖/工具链（C10–C11）与功能补齐（C13–C17）；本文件由随后的 `docs:` 提交引入。

## 本轮验证结果

| 项目 | 命令 | 结果 |
|---|---|---|
| 版本一致性 | `node scripts/version.mjs --check` | PASS — product 1.1 / Android code 4 / taxonomy 1 |
| 脚本测试 | 逐个执行 `node scripts/<name>.test.mjs`（4 个文件） | PASS — 17/17 |
| .NET Debug 构建 | `dotnet build ComponentCatalog.slnx -c Debug` | PASS — 0 error |
| .NET Release 构建 | `dotnet build ComponentCatalog.slnx -c Release` | PASS — 0 error |
| .NET 测试（全量） | `dotnet test ComponentCatalogCore.Tests -c Debug` | 87 通过 / 13 失败（失败项全部为 `CatalogApiServerTests`，环境限制见下） |
| .NET 测试（排除 API 宿主） | `--filter "FullyQualifiedName!~CatalogApiServerTests"`（Debug 与 Release） | PASS — 104/104 |
| Android 测试 | `vitest run --pool=threads` | PASS — 53/53（8 个测试文件） |
| Android 构建 | `npm run build` | PASS |
| Android lint | `npm run lint` | PASS |
| WinUI（含修复后） | `dotnet build ComponentCatalogWinUI -c Release` | PASS — 0 error |
| 原生 Android release 构建 | Gradle 8.14 `assembleRelease`（工作区 staging + 本机缓存） | PASS — `app-release-unsigned.apk` 3,217,859 字节 |
| Java 单元测试 | `java -cp … org.junit.runner.JUnitCore` | PASS — 4/4 |

说明：`NU1903` 高/危漏洞公告（`SQLitePCLRaw.lib.e_sqlite3 2.1.10`，[GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)）已通过 C10 升级 `Microsoft.Data.Sqlite` 至 10.0.14（传递依赖 SQLitePCLRaw 2.1.13）消除，构建现为 0 警告。

## 数据现状

- 当前运行数据在 `ComponentCatalogData/Data/`：`catalog.json` 为 **55 个元件**、taxonomy `v1`、14 个大类，导出时间 2026-07-16；数据库最后写入 2026-07-11。
- `ComponentCatalogData/catalog.db` 与 `ComponentCatalogData/catalog.json` 是迁移前旧快照（29 个元件），`LegacyLocalAppDataBackup/` 是更早的本地备份；两者均为本地数据，保留未删除，运行时以 `Data/` 为准。
- `发布包/Android/app-release.apk`（3,226,051 字节）与 `app-update.json`（`versionCode 4`、含 SHA-256 与签名证书 SHA-256）为当前发布清单；同目录 `电子元件入库台-Android.apk` 是旧包，不在服务器清单内。

## 本机/沙箱环境限制

1. **NuGet 还原**：本机无 NuGet 全局缓存且沙箱下无法写用户目录，需要把 `DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`TEMP/TMP` 指到工作区内（本轮使用 `.hermes-work/` 并通过联网 restore）。
2. **13 个 API 测试受 .NET 10 HttpListener 限制（已定位，非 URL ACL）**：本机 Windows 11（build 26200）上，`.NET 10` 的 `HttpListener.Start()` 在 `SetupV2Config` 调用 `HttpCreateServerSession` 时返回“句柄无效”；已用独立 net10 控制台程序在 runtime **10.0.9 与 10.0.14** 复现，而同地址在 .NET 9 的 PowerShell 进程可正常绑定。属 .NET 10 与当前 Windows 版本的兼容问题，加 URL ACL 或改仓库代码均无法修复；需在目标机复验或向上游报告。其余 104 个测试 Debug/Release 全部通过。
3. **Vitest 默认 forks 池**：当前沙箱下子进程 spawn 被拒（`EPERM`），本轮以 `--pool=threads` 完成 53 例验证；普通开发机直接 `npm test` 即可。
4. **原生 Android 构建验证**：C11 已把 `android-publish.mjs` 改为从 `ANDROID_SDK_ROOT`/`ANDROID_HOME` 解析 SDK 并自动写 `local.properties`，`verify-android-native.ps1` 已参数化 JDK/SDK/暂存/Gradle 目录并强制 JDK ≥ 21。本机沙箱下子进程（Java/Gradle）只能写工作区，因此构建需把项目与 Gradle 缓存放在 `.hermes-work/` 内、Android SDK 只读引用；本机 JDK 17 不足，已临时下载 Microsoft OpenJDK 21 到 `.hermes-work`。原生构建的最终结果见下节“原生 Android 验证”。
5. **WinUI 冒烟启动未执行**：本轮以 Debug/Release 构建和日志/代码走查替代。单实例、删除确认、端口设置等 WinUI 运行时行为未在桌面会话中验证（尝试启动 GUI 时进程未生成启动日志，改为构建级验证）。

## 原生 Android 验证（2026-11-01）

本沙箱下 Java/Gradle 子进程只能写工作区，无法写 `D:\AI` 或 `%TEMP%`，因此原生验证采用“工作区内 staging + 本机 Gradle/Maven 缓存”方式完成：

- 工具链：Microsoft OpenJDK **21.0.13**（临时下载到 `.hermes-work/jdk21`）、Android SDK **36**（`D:\Android\Sdk`，只读引用）、Gradle **8.14**（复制本机缓存到 `.hermes-work`）。
- 构建：`assembleRelease` **BUILD SUCCESSFUL**（123 tasks），产物 `app-release-unsigned.apk` **3,217,859 字节**。
- APK 元数据（aapt，复制到 ASCII 路径后读取）：`com.partshelf.catalog` / `versionCode 4` / `versionName 1.1` / `compileSdk 36` / label `随手查料`。
- 合并清单检查全部通过：`allowBackup=false`、`networkSecurityConfig=@xml/network_security_config`、4 个权限（INTERNET / ACCESS_WIFI_STATE / CHANGE_WIFI_MULTICAST_STATE / REQUEST_INSTALL_PACKAGES）、FileProvider 非导出且 `grantUriPermissions=true`。
- Java 单元测试：Gradle `testDebugUnitTest` 的 Test Executor 在中文路径下启动失败（`ClassNotFoundException: GradleWorkerMain`），改用 `JUnitCore` 直接执行 4/4 通过（[ExampleUnitTest.java](../../Android/android/app/src/test/java/com/getcapacitor/myapp/ExampleUnitTest.java)）。
- 结论：**原生 release 构建链本身可用**；`verify-android-native.ps1` 的正常 ASCII staging 流程在普通开发机应可直接运行，在本沙箱内需要上述手工替代方式。`lintVital` 有一条无法写 `C:\Users\<user>\.android` 的非致命警告，不影响构建。

## 本轮功能补齐（2026-11-01）

- **日志轮转**：新增 [CatalogLogFileWriter.cs](../../ComponentCatalogCore/Diagnostics/CatalogLogFileWriter.cs)（默认单文件 2 MiB、保留 3 个历史文件），`AppLogger` 改用该写入器，避免日志无界增长；5 个单元测试覆盖轮转、保留数量、retention=0 与参数校验。
- **单实例与删除确认**：App 启动时用命名互斥量保证单实例，重复启动会尝试激活已有窗口后退出（[SingleInstanceGuard.cs](../../ComponentCatalogWinUI/Services/SingleInstanceGuard.cs)）；首页删除元件增加二次确认对话框，避免误删。
- **端口可配置**：`lan-settings.json` 升级到 schema v2（默认 HTTP 10849 / UDP 10850，可读取 v1 并自动升级），设置页新增两个端口输入框与校验（范围 1–65535、两者不能相同），工作区服务按配置启动 API/发现服务；核心新增 [CatalogNetworkPorts.cs](../../ComponentCatalogCore/Transport/CatalogNetworkPorts.cs) 与 8 个用例；[Android同步说明.md](../../发布包/Android同步说明.md) 已更新。修改端口后需重启手机访问，并由管理员放行新端口的防火墙/URL ACL。
- **BOM quantity/designators 落库**：`ComponentRecord` 新增 `Quantity`/`Designators`，SQLite 新增 `quantity`/`designators_json` 列（含旧库 `ALTER TABLE` 迁移），CSV/BOM 导入、快照、管理 API、Android 快照契约与详情页全部打通（详情展示“BOM 数量/位号”）；merge 时位号取并集、数量取本次有效值；新增仓储/校验/归一化/预览与 Android 契约测试。导入说明已同步。
- **数据一致性**：新增“快照导出失败后重启自动恢复”测试，覆盖“SQLite 已提交、快照写入失败”的崩溃窗口；`docs/testing/` 历史证据保留不改。
- 本轮新增/更新测试后，.NET 非 API 测试为 **104** 个（Debug/Release 均通过），Android 仍为 **53** 个，scripts 仍为 **17** 个。

## 文档校正

- `发布包/Android同步说明.md`：修正“构建调试 APK / versionCode 2”的过时描述；实际脚本要求外部签名材料并产出 release APK，版本取 `version.json`。
- `Android/README.md`：由 Vite 模板说明改写为本项目 Android 端说明。
- 根 `README.md`：补充数据目录、版本真源与脚本校验命令。

## 后续 backlog

1. **P1 数据一致性（剩余）**：备份恢复目前只有 fake lifecycle 测试，建议补真实宿主生命周期/失败注入测试；数据库与快照双文件替换仍非原子（依靠安全备份和重启恢复兜底）。
2. **P2 Android 只读边界**：Android 端保持只读查询（编辑会破坏“Windows 唯一写源”设计），如确需现场修改，需要先设计管理鉴权与写入 API。
3. **P2 运维**：单实例/可配置端口/日志轮转已补齐；剩余可考虑 Windows 发布产物打包进 `发布包/Windows/`、窗口尺寸与位置记忆。
4. **P3 测试补强**：Java 插件行为、DELETE/PUT、移动端 HEAD/ETag/Range、中文搜索等未覆盖；13 个 API 测试在当前环境被 .NET 10 HttpListener 阻塞（见环境限制）。
5. **局域网安全**：按项目负责人判断，当前明文 HTTP + 短距离可信私网使用可接受，本轮不改造。

已完成（原 backlog）：`Microsoft.Data.Sqlite` 升级消除 `NU1903`（C10）；Android 发布/原生校验脚本路径可配置化（C11）；日志轮转、单实例、删除确认、端口可配置、BOM 数量/位号落库、快照失败恢复测试（C13–C17）。
