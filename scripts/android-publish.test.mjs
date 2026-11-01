import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

import {
  buildReleaseManifest,
  createCommandInvocation,
  ensureAndroidSdkLocalProperties,
  parseApkBadging,
  parseSignerFingerprint,
  publishReleaseAtomically,
  readReleaseSigningContract,
  resolveAndroidPublishStagingDirectory,
  resolveAndroidSdkRoot,
  resolveNpmInvocation,
  validateReleaseArtifact,
  withStagedCandidateApk,
} from "./android-publish.mjs";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

const release = {
  packageName: "com.partshelf.catalog",
  versionCode: 2,
  versionName: "1.1",
  minimumSupportedVersionCode: 1,
  releaseNotes: "版本统一、筛选界面优化与自动更新",
};

const signingEnvironment = {
  CATALOG_RELEASE_STORE_FILE: "D:/private/partshelf-release.jks",
  CATALOG_RELEASE_STORE_PASSWORD: "store-password",
  CATALOG_RELEASE_KEY_ALIAS: "partshelf",
  CATALOG_RELEASE_KEY_PASSWORD: "key-password",
};

test("release signing contract rejects missing or partial credentials", () => {
  assert.throws(
    () => readReleaseSigningContract({ CATALOG_RELEASE_KEY_ALIAS: "partshelf" }),
    /incomplete/i,
  );
  assert.throws(() => readReleaseSigningContract({}), /missing/i);
});

test("release signing contract accepts the complete environment contract without exposing secrets", () => {
  assert.deepEqual(readReleaseSigningContract(signingEnvironment), {
    storeFile: "D:/private/partshelf-release.jks",
    storePassword: "store-password",
    keyAlias: "partshelf",
    keyPassword: "key-password",
  });
});

test("Windows command dispatch runs executables directly and batch files through a quoted cmd.exe command", () => {
  const npm = resolveNpmInvocation({ platform: "win32", nodeExecutable: "C:/Program Files/nodejs/node.exe" });
  assert.equal(npm.command, "C:/Program Files/nodejs/node.exe");
  assert.equal(
    npm.prefixArguments[0].replaceAll("\\", "/"),
    "C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js",
  );

  const executable = createCommandInvocation(
    "D:/Android SDK/build-tools/36.0.0/aapt.exe",
    ["dump", "badging", "D:/AI/component-catalog-publish-staging/candidate.apk"],
    { platform: "win32", commandInterpreter: "C:/Windows/System32/cmd.exe" },
  );
  assert.deepEqual(executable, {
    command: "D:/Android SDK/build-tools/36.0.0/aapt.exe",
    argumentsList: ["dump", "badging", "D:/AI/component-catalog-publish-staging/candidate.apk"],
    spawnOptions: { shell: false, windowsHide: true },
  });

  const batch = createCommandInvocation(
    "D:/Android SDK/build-tools/36.0.0/apksigner.bat",
    ["verify", "D:/AI/component catalog staging/candidate.apk"],
    { platform: "win32", commandInterpreter: "C:/Windows/System32/cmd.exe" },
  );
  assert.deepEqual(batch, {
    command: "C:/Windows/System32/cmd.exe",
    argumentsList: [
      "/d",
      "/s",
      "/c",
      "call \"D:/Android SDK/build-tools/36.0.0/apksigner.bat\" \"verify\" \"D:/AI/component catalog staging/candidate.apk\"",
    ],
    spawnOptions: { shell: false, windowsHide: true, windowsVerbatimArguments: true },
  });
});

test("Unicode candidate APKs are copied to an ASCII staging directory and staging artifacts are cleaned", async () => {
  const directory = await mkdtemp(join(tmpdir(), "partshelf-android-publish-"));
  try {
    const unicodeSourceDirectory = join(directory, "入库");
    const stagingDirectory = join(directory, "component-catalog-publish-staging");
    const candidateApk = join(unicodeSourceDirectory, "app-release.apk");
    await mkdir(unicodeSourceDirectory);
    await writeFile(candidateApk, "signed candidate apk", "utf8");

    assert.equal(
      resolveAndroidPublishStagingDirectory({
        CATALOG_ANDROID_PUBLISH_STAGING_DIR: stagingDirectory,
      }),
      stagingDirectory,
    );
    assert.throws(
      () => resolveAndroidPublishStagingDirectory({ CATALOG_ANDROID_PUBLISH_STAGING_DIR: join(directory, "暂存") }),
      /ASCII-only/i,
    );

    let stagedApk;
    await withStagedCandidateApk(candidateApk, stagingDirectory, async (path) => {
      stagedApk = path;
      assert.doesNotMatch(path, /[^\x00-\x7F]/);
      assert.equal(await readFile(path, "utf8"), "signed candidate apk");
    });

    assert.ok(stagedApk);
    assert.deepEqual(await readdir(stagingDirectory), []);
    await assert.rejects(
      withStagedCandidateApk(candidateApk, stagingDirectory, async () => {
        throw new Error("inspection failed");
      }),
      /inspection failed/,
    );
    assert.deepEqual(await readdir(stagingDirectory), []);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("Android SDK root resolves from the environment with absolute-path validation", () => {
  assert.equal(resolveAndroidSdkRoot({}), "D:/Android/Sdk");
  assert.equal(resolveAndroidSdkRoot({ ANDROID_HOME: "D:/Android/Sdk" }), "D:/Android/Sdk");
  assert.equal(
    resolveAndroidSdkRoot({ ANDROID_SDK_ROOT: "D:/first-sdk", ANDROID_HOME: "D:/second-sdk" }),
    "D:/first-sdk",
  );
  assert.throws(() => resolveAndroidSdkRoot({ ANDROID_SDK_ROOT: "relative/sdk" }), /absolute/i);
});

test("Android SDK local.properties is generated for the resolved SDK path", async () => {
  const directory = await mkdtemp(join(tmpdir(), "partshelf-android-sdk-"));
  try {
    await ensureAndroidSdkLocalProperties(directory, "D:/Android/Sdk");
    assert.equal(
      await readFile(join(directory, "local.properties"), "utf8"),
      "sdk.dir=D:/Android/Sdk\n",
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("APK inspection parses package metadata and the SHA-256 signing certificate", () => {
  assert.deepEqual(
    parseApkBadging("package: name='com.partshelf.catalog' versionCode='2' versionName='1.1' compileSdkVersion='36'"),
    {
      packageName: "com.partshelf.catalog",
      versionCode: 2,
      versionName: "1.1",
    },
  );
  assert.equal(
    parseSignerFingerprint("Signer #1 certificate SHA-256 digest: AABBCCDD"),
    "aabbccdd",
  );
  assert.throws(() => parseApkBadging("package: name='com.partshelf.catalog'"), /versionCode/i);
  assert.throws(() => parseSignerFingerprint("does not contain a certificate"), /SHA-256/i);
});

test("release validation rejects unsigned, wrong-package, and wrong-version artifacts before publication", () => {
  const artifact = {
    packageName: release.packageName,
    versionCode: release.versionCode,
    versionName: release.versionName,
    sha256: "a".repeat(64),
    sizeBytes: 123,
    certificateSha256: "b".repeat(64),
  };

  assert.deepEqual(validateReleaseArtifact(artifact, release), artifact);
  assert.throws(
    () => validateReleaseArtifact({ ...artifact, packageName: "other.package" }, release),
    /package name/i,
  );
  assert.throws(
    () => validateReleaseArtifact({ ...artifact, certificateSha256: "" }, release),
    /certificate/i,
  );
});

test("release manifest contains verified package, version, size, SHA-256, and certificate fingerprint", () => {
  assert.deepEqual(
    buildReleaseManifest(
      {
        packageName: release.packageName,
        versionCode: release.versionCode,
        versionName: release.versionName,
        sha256: "a".repeat(64),
        sizeBytes: 123,
        certificateSha256: "b".repeat(64),
      },
      release,
    ),
    {
      packageName: "com.partshelf.catalog",
      versionCode: 2,
      versionName: "1.1",
      apkPath: "/mobile/app.apk",
      sizeBytes: 123,
      sha256: "a".repeat(64),
      certificateSha256: "b".repeat(64),
      releaseNotes: "版本统一、筛选界面优化与自动更新",
      minimumSupportedVersionCode: 1,
    },
  );
});

test("atomic publishing replaces the APK and manifest only after verified inputs are supplied", async () => {
  const directory = await mkdtemp(join(tmpdir(), "partshelf-android-publish-"));
  try {
    const candidateApk = join(directory, "candidate.apk");
    const hostedApk = join(directory, "app-release.apk");
    const hostedManifest = join(directory, "app-update.json");
    const manifest = buildReleaseManifest(
      {
        packageName: release.packageName,
        versionCode: release.versionCode,
        versionName: release.versionName,
        sha256: "a".repeat(64),
        sizeBytes: 123,
        certificateSha256: "b".repeat(64),
      },
      release,
    );
    await writeFile(candidateApk, "new signed apk", "utf8");
    await writeFile(hostedApk, "old apk", "utf8");
    await writeFile(hostedManifest, "{\"versionCode\":1}", "utf8");

    await publishReleaseAtomically({ candidateApk, hostedApk, hostedManifest, manifest });

    assert.equal(await readFile(hostedApk, "utf8"), "new signed apk");
    assert.deepEqual(JSON.parse(await readFile(hostedManifest, "utf8")), manifest);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("Android release configuration uses secure manifest, explicit Capacitor plugins, limited FileProvider, external signing, and local ignores", async () => {
  const [manifest, activity, filePaths, gradle, ignoreFile, publishCommand] = await Promise.all([
    readFile(join(repositoryRoot, "Android/android/app/src/main/AndroidManifest.xml"), "utf8"),
    readFile(join(repositoryRoot, "Android/android/app/src/main/java/com/partshelf/catalog/MainActivity.java"), "utf8"),
    readFile(join(repositoryRoot, "Android/android/app/src/main/res/xml/file_paths.xml"), "utf8"),
    readFile(join(repositoryRoot, "Android/android/app/build.gradle"), "utf8"),
    readFile(join(repositoryRoot, ".gitignore"), "utf8"),
    readFile(join(repositoryRoot, "发布包/更新安卓安装包到发布包.bat"), "utf8"),
  ]);

  assert.match(manifest, /android:allowBackup="false"/);
  assert.match(manifest, /android:networkSecurityConfig="@xml\/network_security_config"/);
  assert.match(activity, /registerPlugin\(AppUpdatePlugin\.class\)/);
  assert.match(activity, /registerPlugin\(CatalogDiscoveryPlugin\.class\)/);
  assert.ok(activity.indexOf("registerPlugin(AppUpdatePlugin.class)") < activity.indexOf("super.onCreate(savedInstanceState)"));
  assert.ok(activity.indexOf("registerPlugin(CatalogDiscoveryPlugin.class)") < activity.indexOf("super.onCreate(savedInstanceState)"));
  assert.match(filePaths, /<cache-path name="updates" path="updates\/" \/>/);
  assert.doesNotMatch(filePaths, /external-path/);
  assert.match(gradle, /CATALOG_RELEASE_PROPERTIES_FILE/);
  assert.match(gradle, /CATALOG_RELEASE_STORE_FILE/);
  assert.match(gradle, /signingConfigs/);
  assert.match(ignoreFile, /ComponentCatalogData\/\*\*\/catalog\.json/);
  assert.match(ignoreFile, /ComponentCatalogData\/\*\*\/api\.token/);
  assert.match(ignoreFile, /\*signing\*\.properties/);
  assert.match(publishCommand, /android-publish\.mjs/);
  assert.doesNotMatch(publishCommand, /assembleDebug/);
});
