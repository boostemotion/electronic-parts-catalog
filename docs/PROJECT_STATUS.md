# 项目状态（2026-11-01）

当前状态摘要。完整验证记录见 [verification/2026-11-01-verification.md](verification/2026-11-01-verification.md)，文档索引见 [README.md](README.md)。

## 当前基线

- 分支 `main`，无 remote，未 push；工作区干净。
- 本轮整理出 **18 个本地提交**（下表），本次文档整理为随后一个独立 docs 提交；整理前的工作区 checkpoint `c4975b9` 仍可通过 reflog 找回。
- 测试：.NET 非 API **104/104**（Debug 与 Release）、Android **53/53**、scripts **17/17**；13 个 `CatalogApiServerTests` 受本机 .NET 10 HttpListener 兼容问题阻塞（见验证记录）。
- 原生 Android release 构建通过：`app-release-unsigned.apk` 3,217,859 字节，`versionCode 4` / `versionName 1.1`。
- 版本单一真源 `version.json`：product `1.1` / Android code `4` / taxonomy `1`。

## 提交（按主题）

| 主题 | 提交 |
|---|---|
| 基线整理 | `7539a4a`、`a905022`、`c11dc16`、`330c627`、`402b972`、`b0981de` |
| 修复 | `071c579`（导入页双仓储）、`f92cff7`（包过滤大小写） |
| 依赖与工具链 | `15df6c7`（SQLite 10.0.14，消除 NU1903）、`a80432c`（Android 脚本路径可配置） |
| 功能补齐 | `7ffcdea`（日志轮转）、`2001b91`（单实例+删除确认）、`332e27a`（BOM 数量/位号落库）、`0718e41`（端口可配置）、`96995e9`（快照恢复测试） |
| 状态文档 | `a85550f`、`6693587`、`8a17bed`、本次文档整理提交 |

## 运行数据

- 当前数据在 `ComponentCatalogData/Data/`：`catalog.json` 为 **55 个元件**、taxonomy `v1`、14 个大类，导出时间 2026-07-16；数据库最后写入 2026-07-11。
- `ComponentCatalogData/catalog.db` 与 `catalog.json` 是迁移前旧快照（29 个元件），`LegacyLocalAppDataBackup/` 是更早的本地备份；均为本地数据，保留未删除，运行时以 `Data/` 为准。
- `发布包/Android/app-release.apk`（3,226,051 字节）与 `app-update.json`（`versionCode 4`、含 SHA-256 与签名证书 SHA-256）为当前发布清单；同目录 `电子元件入库台-Android.apk` 是旧包，不在服务器清单内。

## 已完成能力（本轮补齐）

- 单实例保护（防并发写库）与删除元件二次确认。
- HTTP/UDP 端口可配置（设置页 + 校验 + 旧配置自动升级），默认 10849 / 10850。
- 日志按 2 MiB × 3 份轮转，避免无界增长。
- BOM `quantity` / `designators` 落库，贯穿 SQLite、快照、管理 API 与 Android 详情展示。
- 快照导出失败后的重启自动恢复测试（覆盖 SQLite 已提交、快照写入失败的窗口）。
- `Microsoft.Data.Sqlite` 升级至 10.0.14，消除 `NU1903` 高危公告。

## 环境限制（摘要，详见验证记录）

- 本机 .NET 10 HttpListener 与 Windows 11 build 26200 不兼容，13 个 API 测试无法在本机跑通（非 URL ACL、非代码问题）。
- NuGet/Vitest/GUI 冒烟受沙箱限制：使用工作区缓存、`--pool=threads`、构建级验证替代；`.hermes-work/` 约 5.3 GB 临时缓存可删除。
- 原生 Android 构建使用临时 JDK 21 与本机 Gradle/Maven 缓存完成，未作为常规门禁。

## 后续 backlog

1. **P1 数据一致性（剩余）**：备份恢复仍只有 fake lifecycle 测试，建议补真实宿主生命周期/失败注入测试；数据库与快照双文件替换仍非原子（依靠安全备份和重启恢复兜底）。
2. **P2 Android 只读边界**：保持只读查询；如确需现场修改，需要先设计管理鉴权与写入 API。
3. **P2 Windows 收尾**：窗口尺寸/位置记忆、BOM 数量/位号的编辑 UI、Windows 发布产物打包。
4. **P2 Android 体验优化**：列表入场动画线性延迟、未定义 MD3 CSS 变量、`motion.css` 未接入、缺失排序与无障碍属性、同步失败提示与重试。
5. **P3 测试补强**：Java 插件行为、DELETE/PUT、移动端 HEAD/ETag/Range、中文搜索等；以及上述 .NET 10 HttpListener 阻塞的 13 个 API 测试需在目标机复验。
6. **局域网安全**：按项目负责人判断，当前明文 HTTP + 短距离可信私网使用可接受，暂不改造。
