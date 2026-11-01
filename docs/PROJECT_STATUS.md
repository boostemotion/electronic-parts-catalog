# 项目状态（2026-11-01）

本文件记录本轮“验证基线 + 分组提交 + 文档校正”后的状态快照，供后续开发与排查参考。

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

- 整理前的完整工作区 checkpoint 为 `c4975b9`（不在 main 历史中，仍可通过 reflog 找回）。
- 上表为基线（C1–C6）与修复（C7–C8）；本文件由随后的 `docs:` 提交引入。

## 本轮验证结果

| 项目 | 命令 | 结果 |
|---|---|---|
| 版本一致性 | `node scripts/version.mjs --check` | PASS — product 1.1 / Android code 4 / taxonomy 1 |
| 脚本测试 | 逐个执行 `node scripts/<name>.test.mjs`（4 个文件） | PASS — 15/15 |
| .NET Debug 构建 | `dotnet build ComponentCatalog.slnx -c Debug` | PASS — 0 error |
| .NET Release 构建 | `dotnet build ComponentCatalog.slnx -c Release` | PASS — 0 error |
| .NET 测试（全量） | `dotnet test ComponentCatalogCore.Tests -c Debug` | 87 通过 / 13 失败（失败项全部为 `CatalogApiServerTests`，环境限制见下） |
| .NET 测试（排除 API 宿主） | `--filter "FullyQualifiedName!~CatalogApiServerTests"`（Debug 与 Release） | PASS — 85/85 |
| Android 测试 | `vitest run --pool=threads` | PASS — 53/53（8 个测试文件） |
| Android 构建 | `npm run build` | PASS |
| Android lint | `npm run lint` | PASS |
| WinUI（含修复后） | `dotnet build ComponentCatalogWinUI -c Release` | PASS — 0 error |

说明：`.NET` 构建出现 `NU1903` 警告——`SQLitePCLRaw.lib.e_sqlite3 2.1.10` 存在高危漏洞公告 [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)，属既有依赖问题。

## 数据现状

- 当前运行数据在 `ComponentCatalogData/Data/`：`catalog.json` 为 **55 个元件**、taxonomy `v1`、14 个大类，导出时间 2026-07-16；数据库最后写入 2026-07-11。
- `ComponentCatalogData/catalog.db` 与 `ComponentCatalogData/catalog.json` 是迁移前旧快照（29 个元件），`LegacyLocalAppDataBackup/` 是更早的本地备份；两者均为本地数据，保留未删除，运行时以 `Data/` 为准。
- `发布包/Android/app-release.apk`（3,226,051 字节）与 `app-update.json`（`versionCode 4`、含 SHA-256 与签名证书 SHA-256）为当前发布清单；同目录 `电子元件入库台-Android.apk` 是旧包，不在服务器清单内。

## 本机/沙箱环境限制

1. **NuGet 还原**：本机无 NuGet 全局缓存且沙箱下无法写用户目录，需要把 `DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`TEMP/TMP` 指到工作区内（本轮使用 `.hermes-work/` 并通过联网 restore）。
2. **13 个 API 测试受 HttpListener 限制**：VSTest 测试宿主内 `HttpListener.Start()` 抛 `HttpListenerException: 句柄无效`（`SetupV2Config`），同一地址在独立进程可正常绑定；需要管理员配置 URL ACL 或以提权测试宿主复跑，才能凑齐 100/100。
3. **Vitest 默认 forks 池**：当前沙箱下子进程 spawn 被拒（`EPERM`），本轮以 `--pool=threads` 完成 53 例验证；普通开发机直接 `npm test` 即可。
4. **原生 Android 构建未验证**：需要 JDK 21（本机只有 JDK 17）、Android SDK 36 与 ASCII 构建路径；`scripts/verify-android-native.ps1` 仍固定 `C:\Program Files\Java\jdk-21.0.10` 等绝对路径，`Android/android/local.properties` 指向不存在的 `D:\Android\Sdk`，需下一轮先做工具链可移植化。
5. **WinUI 冒烟启动未执行**：本轮以 Debug/Release 构建和日志/代码走查替代，未在桌面会话中启动 GUI。

## 文档校正

- `发布包/Android同步说明.md`：修正“构建调试 APK / versionCode 2”的过时描述；实际脚本要求外部签名材料并产出 release APK，版本取 `version.json`。
- `Android/README.md`：由 Vite 模板说明改写为本项目 Android 端说明。
- 根 `README.md`：补充数据目录、版本真源与脚本校验命令。

## 后续 backlog（按优先级）

1. **P1 数据一致性**：SQLite 写入与快照导出仍是两步（崩溃窗口靠启动重导出兜底）；备份恢复的双文件替换非原子。建议补真实宿主生命周期测试与失败注入测试。
2. **P1 局域网安全**：管理/移动接口均为明文 HTTP，UDP 发现响应不校验源 IP；APK 清单与下载同源、无证书固定。仅可在可信私网使用，后续考虑 TLS 或配对码方案。
3. **P2 依赖安全**：升级 `Microsoft.Data.Sqlite` / `SQLitePCLRaw` 消除 `NU1903` 高危公告并回归。
4. **P2 原生 Android 工具链可移植化**：`android-publish.mjs` 的 SDK 路径改为读取 `ANDROID_SDK_ROOT`/`ANDROID_HOME`；`verify-android-native.ps1` 参数化 JDK/暂存/Gradle 目录；修正 `local.properties` 生成方式。
5. **P2 功能/运维**：端口 10849/10850 硬编码、无应用级单实例、日志无轮转、BOM 的 `quantity/designators` 未落库、Android 端无编辑能力。
6. **P3 测试补强**：Android 部分测试为 CSS/源码字符串断言；Java 插件行为、DELETE/PUT、移动端 HEAD/ETag/Range、中文搜索等未覆盖。
