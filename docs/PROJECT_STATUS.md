# 项目状态（2026-11-01）

当前状态摘要。完整验证记录见 [verification/2026-11-01-verification.md](verification/2026-11-01-verification.md)，文档索引见 [README.md](README.md)。最后整理：2026-11-01（隐私历史重写后）。

## 当前基线

- 分支 `main`，远程 `origin` = `https://github.com/boostemotion/electronic-parts-catalog.git`（公开），已推送；工作区干净。
- `main` 已完成基线整理、功能补齐、Android 优化与隐私历史重写（提交数量以 `git log` 为准）。2026-11-01 按隐私要求重写历史：作者/提交者统一为 `boostemotion <noreply>`，并清理历史中的本机路径、旧内网地址与真实数据引用；旧提交哈希已失效，以 `git log` 为准。
- 测试：.NET 非 API **104/104**（Debug 与 Release）、Android **62/62**、scripts **17/17**；13 个 `CatalogApiServerTests` 受本机 .NET 10 HttpListener 兼容问题阻塞（见验证记录）。
- 原生 Android release 构建通过（含本轮 Web 资源同步）：`app-release-unsigned.apk` 3,219,895 字节，`versionCode 4` / `versionName 1.1`。
- 版本单一真源 `version.json`：product `1.1` / Android code `4` / taxonomy `1`。

## 提交（按主题）

提交历史按主题覆盖：基线整理、修复、依赖与工具链、功能补齐、Android 体验优化、状态与隐私文档。具体提交列表请以 `git log --oneline` 为准；隐私历史重写后旧哈希已失效。

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

## Android 查询端体验优化（2026-11-01）

- **性能/动效**：列表卡片入场延迟由线性 `280 + index×36ms` 改为 motion.css 的有限阶梯（最多 144ms），55 条数据不再等待约 2.3 秒；统一由 `motion.css` 接管页面转场、卡片、弹层与按压反馈，并保留 `prefers-reduced-motion` 降级。
- **样式修复**：补齐 7 个此前未定义的 MD3 变量（`surface-container-low`、`outline-variant`、`error`、`error-container`、`tertiary-container`、`on-tertiary-container`、`motion-easing-emphasized`），边框/错误提示/徽标颜色恢复生效；新增样式契约测试防止回归。
- **排序**：筛选面板新增“匹配度 / 名称 / 型号 / 分类”四种排序，与筛选条件组合生效，重置筛选会同时恢复匹配度排序。
- **无障碍**：详情与筛选面板补齐 `role="dialog"` / `aria-modal` / `aria-label`，详情支持 Esc 关闭与可见关闭按钮；搜索框增加 `aria-label`。
- **同步与更新**：新增“设备同步”时间（跨启动保留）、同步失败可重试更新检查按钮；更新提示“稍后”选择持久化到 localStorage，同一版本不再反复弹出。
- **清理**：删除未接入的 `ComponentEditSheet.tsx`、`AppDialog.tsx` 死代码；`layout-scroll.test.ts` 改为断言当前实际使用的同步面板样式。
- **深色主题**：`index.css` 增加完整的 `prefers-color-scheme: dark` 调色板（surface/on-surface/primary/error/inverse 等 20+ 变量），`surface-mix` 统一替代硬编码白色混合，snackbar 改用 inverse 色；`index.html` 提供双 `theme-color`；原生侧增加 `values-night/colors.xml`，`MainActivity` 根据 `UI_MODE_NIGHT` 自动切换系统栏图标明暗；更新/错误/状态徽标均有暗色适配。
- **动效细调**：动效 token 收敛为 90–300ms 分级（short2 90ms、medium2 220ms、medium4 300ms）；页面转场位移 40→24px、卡片阶梯 30ms×10→18ms×8（封顶 144ms）、卡片位移 20→12px、弹层进出 400/250→300/180ms、按压缩放 0.97→0.985、导航图标 1.1→1.06；均保留 `prefers-reduced-motion` 降级。

## WinUI 性能与交互优化（2026-11-02）

- **筛选结果批量替换**：`FilteredItems` 由逐项 `Clear/Add` 的 `ObservableCollection` 改为一次性替换的只读列表，侧边栏切换分类时 GridView 只重建一次（原来每移除/添加一条都会触发容器回收），显著降低卡顿。
- **筛选切换动效**：新增 `UiMotion.PlayListRefresh`，分类切换后对结果区做 150–170ms 的淡入 + 轻微上移动画，替代原来“元件直接消失”的生硬切换；搜索输入时不做该动画以免干扰。
- **侧边栏事件去抖**：刷新子类列表时临时抑制 ComboBox 的 `SelectionChanged`，避免一次点击触发多轮筛选；编辑面板的大类/子类刷新增加递归保护。
- **侧边栏下拉复用**：封装与持有状态 ComboBox 的 `ItemsSource` 只在首次绑定，不再每次筛选都重设，减少无谓的列表重建。

## 环境限制（摘要，详见验证记录）

- 本机 .NET 10 HttpListener 与 Windows 11 build 26200 不兼容，13 个 API 测试无法在本机跑通（非 URL ACL、非代码问题）。
- NuGet/Vitest/GUI 冒烟受沙箱限制：使用工作区缓存、`--pool=threads`、构建级验证替代；`.hermes-work/` 约 5.3 GB 临时缓存可删除。
- 原生 Android 构建使用临时 JDK 21 与本机 Gradle/Maven 缓存完成，未作为常规门禁。

## 后续 backlog

1. **P1 数据一致性（剩余）**：备份恢复仍只有 fake lifecycle 测试，建议补真实宿主生命周期/失败注入测试；数据库与快照双文件替换仍非原子（依靠安全备份和重启恢复兜底）。
2. **P2 Android 只读边界**：保持只读查询；如确需现场修改，需要先设计管理鉴权与写入 API。
3. **P2 Windows 收尾**：窗口尺寸/位置记忆、BOM 数量/位号的编辑 UI、Windows 发布产物打包。
4. **P3 测试补强**：Java 插件行为、DELETE/PUT、移动端 HEAD/ETag/Range、中文搜索等；以及上述 .NET 10 HttpListener 阻塞的 13 个 API 测试需在目标机复验。
5. **局域网安全**：按项目负责人判断，当前明文 HTTP + 短距离可信私网使用可接受，暂不改造。
