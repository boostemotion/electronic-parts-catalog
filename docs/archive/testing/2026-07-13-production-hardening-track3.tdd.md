# Track 3 Android release hardening — TDD evidence

> **历史文档（2026-07-13，已归档）**
> 文中版本号（versionCode 2）与测试数量是当时记录，现已过时；仅作 TDD 过程证据保留。
> 当前状态与验证结果见 [../../PROJECT_STATUS.md](../../PROJECT_STATUS.md)。

## Source plan

- `.hermes/plans/2026-07-13_005540-production-hardening.md`

The plan was treated as untrusted input. This implementation excludes Track 4 version changes: Android remains product version `1.1`, version code `2`.

## User journeys

1. As a mobile user, I want the installed app to block Android backup and use the intended network-security policy, so local catalog access follows the documented Android security boundary.
2. As a mobile user, I want native Capacitor update and discovery plugins to be registered before bridge startup, so their JavaScript APIs are available.
3. As a mobile user, I want update APK sharing restricted to the cache `updates/` directory, so the installer cannot expose arbitrary external files.
4. As a release operator, I want builds to use complete signing material from outside Git and reject incomplete configuration, so an unsigned or partially configured release cannot be published.
5. As a release operator, I want APK package/version/signature/hash metadata checked before staged publication, so the hosted update describes the exact signed artifact.

## RED → GREEN evidence

| Stage | Command | Result |
|---|---|---|
| RED | `node --test scripts/android-publish.test.mjs` | Failed with `ERR_MODULE_NOT_FOUND` for the absent `scripts/android-publish.mjs`, proving the new behavior was not yet implemented. |
| GREEN | `node --test scripts/android-publish.test.mjs` | 7 passed, 0 failed. |

## Guarantees proved

| # | Guarantee | Test / evidence | Result |
|---|---|---|---|
| 1 | Missing or incomplete direct signing variables are rejected. | `scripts/android-publish.test.mjs` — signing contract test | PASS |
| 2 | Complete direct signing variables are accepted without emitting their values. | `scripts/android-publish.test.mjs` — signing contract test | PASS |
| 3 | APK package metadata and SHA-256 certificate output are parsed. | `scripts/android-publish.test.mjs` — APK inspection parsing test | PASS |
| 4 | Wrong package, version, absent signer fingerprint, invalid hash, and invalid size prevent release publication. | `scripts/android-publish.test.mjs` — validation test | PASS |
| 5 | Generated update metadata includes the package, Android version, APK size/SHA-256, and signing-certificate SHA-256. | `scripts/android-publish.test.mjs` — manifest test | PASS |
| 6 | APK and manifest are first staged in the target directory and are only renamed after staged writes succeed. | `scripts/android-publish.test.mjs` — staged publication test | PASS |
| 7 | The static Android release configuration declares the network-security reference, disabled backup, explicit custom plugin registration, restricted provider path, external signing contract, protected ignore rules, and release-only wrapper. | `scripts/android-publish.test.mjs` — static configuration test | PASS |

## Validation performed

| Command | Result |
|---|---|
| `node --test scripts/version.test.mjs` | PASS — 4 tests |
| `node scripts/version.mjs --check` | PASS — product `1.1`, Android code `2`, taxonomy `1` |
| `node --test scripts/android-publish.test.mjs` | PASS — 7 tests |
| `env -u CATALOG_RELEASE_STORE_FILE -u CATALOG_RELEASE_STORE_PASSWORD -u CATALOG_RELEASE_KEY_ALIAS -u CATALOG_RELEASE_KEY_PASSWORD -u CATALOG_RELEASE_PROPERTIES_FILE node scripts/android-publish.mjs` | Expected failure before build/publication: signing configuration missing |
| `npm --prefix Android test` | PASS — 48 tests in 7 files |
| `npm --prefix Android run lint` | PASS |
| `npm --prefix Android run build && npm --prefix Android run android:sync` | PASS |
| `Android/android/gradlew.bat -p Android/android assembleRelease --no-daemon` | PASS — unsigned fallback produced `app-release-unsigned.apk` |
| `dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj --no-restore` | PASS — 81 tests |
| `dotnet build ComponentCatalogWinUI/ComponentCatalogWinUI.csproj --no-restore` | PASS — 0 warnings, 0 errors |

## Merged-manifest evidence

The release merged manifest at `Android/android/app/build/intermediates/merged_manifests/release/processReleaseManifest/AndroidManifest.xml` contains:

- `android:allowBackup="false"`
- `android:networkSecurityConfig="@xml/network_security_config"`
- `android:versionCode="2"`
- `android:versionName="1.1"`
- the non-exported FileProvider that references `@xml/file_paths`

The provider paths file contains only `<cache-path name="updates" path="updates/" />`.

## Coverage and known gaps

The Node publisher tests use Node's built-in test runner and cover the external behavior above. No coverage tool is configured for this standalone ESM script.

A real signed end-to-end publication is intentionally blocked until a stable external keystore exists. The required completion input is either all four `CATALOG_RELEASE_*` variables or `CATALOG_RELEASE_PROPERTIES_FILE` pointing outside the repository to a properties file that supplies `storeFile`, `storePassword`, `keyAlias`, and `keyPassword`. No key, password, or token was created, read into a report, committed, or published during this work.

No checkpoint commits were created because the requested work explicitly prohibits committing or pushing.
