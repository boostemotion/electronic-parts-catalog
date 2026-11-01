# Android 局域网同步与更新说明

Windows 端 SQLite 是唯一写入真源。默认 API 只监听 `http://127.0.0.1:10849`，不会向局域网公开。

## 启用手机自动同步

1. 在 Windows 程序的“设置”中打开“允许手机自动发现和同步”。
2. 程序会只绑定所选私有 IPv4 的 HTTP 端口（默认 `10849`），并启用 UDP 发现响应（默认 `10850`）；端口可在“设置 → 高级诊断”中修改。修改后需重启手机访问，并由管理员同步放行防火墙和 URL ACL。手机与 Windows 连接同一可信 Wi‑Fi 后会自动发现并同步。
3. Android 只会无授权请求 `GET /mobile/snapshot`，获得完整 taxonomy v1 目录快照；不会新增、编辑、删除或导入正式目录。

关闭该开关会停止局域网 HTTP 和 UDP 发现服务，本机 Bearer 管理 API 仍可继续使用。Android 不需要、不会保存，也不会要求输入地址或 Token。

局域网访问仅适用于可信私有网络。当前移动同步和 APK 更新使用 HTTP 而非 TLS；请勿在公共网络使用。

## Android 更新发布

Windows 仅从下列固定文件提供 Android 更新；服务器不会根据 URL 参数选择文件：

- `发布包/Android/app-release.apk`
- `发布包/Android/app-update.json`

在项目根目录运行 `发布包/更新安卓安装包到发布包.bat`，或在 Windows 程序“设置”的“Android 安装包发布”卡片中选择“重新生成 Android 安装包”。脚本要求提供外部签名材料（4 个 `CATALOG_RELEASE_*` 环境变量，或一个位于仓库外、包含 `storeFile`/`storePassword`/`keyAlias`/`keyPassword` 的 properties 文件），随后构建 **release** APK，经 zipalign、apksigner、aapt 校验后按 `version.json` 的版本（当前 `1.1` / `versionCode` `4`）、字节数、APK SHA-256 与签名证书 SHA-256 写入 UTF-8 清单；未配置签名时脚本会直接失败，不会发布调试包。

启用手机访问且该 APK 存在时，固定只读端点为：

- `GET` 或 `HEAD` `/mobile/app-update`：更新清单（`Cache-Control: no-store`）
- `GET` 或 `HEAD` `/mobile/app.apk`：APK 字节流（`application/vnd.android.package-archive`）

Android 只在目录同步成功后检查清单，并且仅在清单中的 `versionCode` 高于已安装版本时显示更新提示。选择“更新”后，应用会下载到私有缓存目录，拒绝重定向、校验精确字节数和 SHA-256，然后交由 Android **系统安装器**确认。应用不会静默安装。

若 Android 尚未授予“允许来自此来源的应用”权限，应用会打开当前应用的系统设置页并明确提示；授予权限后，用户需再次点击“更新”。请只在受信任的私有网络和受控的 Windows 主机上使用此更新功能。

## 连接失败

Windows 不会自动提升权限、添加 URL ACL 或修改防火墙。若 Windows 设置页“高级诊断”提示 URL ACL 或防火墙阻止，请由管理员只针对所选明确地址和专用网络配置规则；不要使用通配监听地址。

UDP 广播可能被 AP 隔离或路由器策略阻止。确认同一 Wi‑Fi、Windows 的手机访问状态为“已开放只读查询”，然后在 Android “同步”页选择“重新查找电脑”。
