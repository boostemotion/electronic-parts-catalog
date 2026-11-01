import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const verificationScript = join(repositoryRoot, "scripts", "verify-android-native.ps1");

test("native Android verification gate supports configurable toolchain paths and validates release contracts", async () => {
  const script = await readFile(verificationScript, "utf8");

  assert.match(script, /\[string\]\$JdkHome/);
  assert.match(script, /\[string\]\$AndroidSdk/);
  assert.match(script, /\[string\]\$StageRoot/);
  assert.match(script, /\[string\]\$GradleUserHome/);
  assert.match(script, /ANDROID_SDK_ROOT/);
  assert.match(script, /ANDROID_HOME/);
  assert.match(script, /sdk\.dir=/);
  assert.match(script, /\$env:JAVA_HOME\s*=\s*\$jdkHome/);
  assert.match(script, /\$env:GRADLE_USER_HOME\s*=\s*\$gradleUserHome/);
  assert.match(script, /testDebugUnitTest/);
  assert.match(script, /assembleRelease/);
  assert.match(script, /allowBackup/);
  assert.match(script, /networkSecurityConfig/);
  assert.match(script, /android\.permission\.ACCESS_WIFI_STATE/);
  assert.match(script, /android\.permission\.CHANGE_WIFI_MULTICAST_STATE/);
  assert.match(script, /android\.permission\.REQUEST_INSTALL_PACKAGES/);
  assert.match(script, /FileProvider/);
  assert.match(script, /AppUpdatePlugin/);
  assert.match(script, /CatalogDiscoveryPlugin/);
  assert.doesNotMatch(script, /CATALOG_RELEASE_(?:STORE_FILE|STORE_PASSWORD|KEY_ALIAS|KEY_PASSWORD)\s*=/);
});
