# 验证记录（2026-11-01）

本文件是 [PROJECT_STATUS.md](../PROJECT_STATUS.md) 的详细验证附录，记录当时的环境、命令与结果。

## 环境与前置

- Windows 11（build 26200）；仓库内 `.dotnet-sdk/` 提供 .NET SDK **10.0.301**（目标 `net10.0`）。
- 本机无 NuGet 全局缓存且沙箱限制用户目录写入，.NET 命令统一把 `DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`NUGET_HTTP_CACHE_PATH`、`TEMP`/`TMP` 指向 `.hermes-work/`（该目录已被 `.gitignore` 忽略）。
- Node.js **24.18.1** / npm **11.16.0**；`Android/node_modules` 已存在，无需重新安装。
- 原生 Android：Microsoft OpenJDK **21.0.13**（临时下载到 `.hermes-work/jdk21`）、Android SDK **36**（`D:\Android\Sdk`，只读引用）、Gradle **8.14**（由本机缓存复制到 `.hermes-work`）。
- Git：分支 `main`，远程 `origin` 已配置并推送（`https://github.com/boostemotion/electronic-parts-catalog.git`）。

## 结果总表

| 项目 | 命令 | 结果 |
|---|---|---|
| 版本一致性 | `node scripts/version.mjs --check` | PASS — product 1.1 / Android code 4 / taxonomy 1 |
| 脚本测试 | 逐个执行 `node scripts/<name>.test.mjs`（4 个文件） | PASS — 17/17 |
| .NET Debug 构建 | `dotnet build ComponentCatalog.slnx -c Debug` | PASS — 0 error、0 warning |
| .NET Release 构建 | `dotnet build ComponentCatalog.slnx -c Release` | PASS — 0 error、0 warning |
| .NET 测试（排除 API 宿主）Debug | `dotnet test ComponentCatalogCore.Tests -c Debug --filter "FullyQualifiedName!~CatalogApiServerTests"` | PASS — 104/104 |
| .NET 测试（排除 API 宿主）Release | 同上，`-c Release` | PASS — 104/104 |
| .NET 全量测试 | 不排除过滤 | 13 个 `CatalogApiServerTests` 受环境限制失败（见下），其余通过 |
| Android 测试 | `vitest run --pool=threads`（工作区内） | PASS — 62/62（10 个测试文件） |
| Android 构建 | `npm run build` | PASS |
| Android lint | `npm run lint` | PASS |
| WinUI Release 构建 | `dotnet build ComponentCatalogWinUI -c Release` | PASS — 0 error |
| 原生 Android release 构建 | Gradle 8.14 `assembleRelease`（工作区 staging + 本机缓存） | PASS — `app-release-unsigned.apk` 3,219,895 字节（含深色主题与动效优化后的 Web 资源） |
| Java 单元测试 | `java -cp … org.junit.runner.JUnitCore com.getcapacitor.myapp.ExampleUnitTest` | PASS — 4/4 |

`NU1903` 高危公告（`SQLitePCLRaw.lib.e_sqlite3 2.1.10`，[GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)）已通过升级 `Microsoft.Data.Sqlite` 至 10.0.14（传递依赖 SQLitePCLRaw 2.1.13）消除。

## 13 个 API 测试的环境限制（已定位，非 URL ACL）

- 本机 Windows 11 build 26200 上，`.NET 10` 的 `HttpListener.Start()` 在 `SetupV2Config` 调用 `HttpCreateServerSession` 时返回“句柄无效”。
- 已用独立 net10 控制台程序在 runtime **10.0.9 与 10.0.14** 复现；同一地址在 .NET 9 的 PowerShell 进程可正常绑定。
- 结论：属 .NET 10 与当前 Windows 版本的兼容问题，加 URL ACL 或改仓库代码均无法修复；需在目标部署机复验或向 .NET 上游报告。除这 13 个用例外的 104 个测试在 Debug/Release 下全部通过。

## 原生 Android 验证

本沙箱下 Java/Gradle 子进程只能写工作区、无法写 `D:\AI` 或 `%TEMP%`，因此原生验证采用“工作区内 staging + 本机 Gradle/Maven 缓存”方式完成：

- 构建：`assembleRelease` **BUILD SUCCESSFUL**（123 tasks），干净 staging 产物 `app-release-unsigned.apk` **3,219,895 字节**（深色主题与动效优化后的 Web 资源已通过 `npm run android:sync` 同步；`values-night/colors.xml` 与深色系统栏逻辑已包含在构建中）。
- APK 元数据（aapt，复制到 ASCII 路径后读取）：`com.partshelf.catalog` / `versionCode 4` / `versionName 1.1` / `compileSdk 36` / label `随手查料`。
- 合并清单检查全部通过：`allowBackup=false`、`networkSecurityConfig=@xml/network_security_config`、4 个权限（INTERNET / ACCESS_WIFI_STATE / CHANGE_WIFI_MULTICAST_STATE / REQUEST_INSTALL_PACKAGES）、FileProvider 非导出且 `grantUriPermissions=true`。
- Java 单元测试：Gradle `testDebugUnitTest` 的 Test Executor 在中文路径下启动失败（`ClassNotFoundException: GradleWorkerMain`），改用 `JUnitCore` 直接执行 **4/4** 通过（[ExampleUnitTest.java](../../Android/android/app/src/test/java/com/getcapacitor/myapp/ExampleUnitTest.java)）。
- 结论：原生 release 构建链可用；[verify-android-native.ps1](../../scripts/verify-android-native.ps1) 的正常 ASCII staging 流程在普通开发机应可直接运行，本沙箱内需要上述手工替代方式。`lintVital` 有一条无法写 `C:\Users\<user>\.android` 的非致命警告，不影响构建。

## 沙箱/环境限制（摘要）

1. **NuGet 还原**：需要联网并把缓存/临时目录指向工作区 `.hermes-work/`。
2. **Java/Gradle 子进程写入范围**：只能写工作区；因此 staging、Gradle Home、Gradle 发行版都放在 `.hermes-work/` 内。
3. **Vitest 默认 forks 池**：当前沙箱下子进程 spawn 被拒（`EPERM`），使用 `--pool=threads` 完成验证；普通开发机直接 `npm test` 即可。
4. **WinUI GUI 冒烟未执行**：尝试启动未产生启动日志，改为 Debug/Release 构建与代码走查验证。
5. `.hermes-work/` 约 5.3 GB（NuGet 包、JDK 21、Gradle 缓存/发行版、staging），已被忽略，可随时删除。

## Android 查询端优化验证（2026-11-01 补充）

- `npm test`（`vitest run --pool=threads`）：**62/62**（10 个文件，新增排序、样式契约、深色主题、无障碍相关用例）。
- `npm run build`：PASS（CSS 30.9 → 38.3 KB，含接入的 `motion.css` 与深色变量；JS 235.8 → 238.8 KB）。
- `npm run lint`：PASS。
- `npm run android:sync`：PASS（新 Web 资源已复制到 `Android/android/app/src/main/assets/public`，该目录由 `Android/android/.gitignore` 忽略）。
- 原生 `assembleRelease`：PASS，干净 staging 构建 APK **3,219,895 字节**（2026-11-01）。

## 复跑建议

```powershell
# Android Web
npm --prefix Android test
npm --prefix Android run build
npm --prefix Android run lint

# scripts 与版本
node scripts/version.mjs --check
node --test scripts/

# .NET（在具备正常 NuGet 缓存的机器上）
dotnet test ComponentCatalogCore.Tests
dotnet build ComponentCatalog.slnx -c Release
```
