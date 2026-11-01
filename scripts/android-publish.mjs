import { createHash, randomUUID } from "node:crypto";
import { createReadStream } from "node:fs";
import {
  access,
  copyFile,
  mkdir,
  readdir,
  readFile,
  rename,
  rm,
  stat,
  writeFile,
} from "node:fs/promises";
import { dirname, isAbsolute, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const DEFAULT_ANDROID_SDK_ROOT = "D:/Android/Sdk";
const APK_PATH = "/mobile/app.apk";
const PACKAGE_NAME = "com.partshelf.catalog";
const DEFAULT_STAGING_DIRECTORY = "D:/AI/component-catalog-publish-staging";
const SIGNING_FIELDS = [
  ["storeFile", "CATALOG_RELEASE_STORE_FILE"],
  ["storePassword", "CATALOG_RELEASE_STORE_PASSWORD"],
  ["keyAlias", "CATALOG_RELEASE_KEY_ALIAS"],
  ["keyPassword", "CATALOG_RELEASE_KEY_PASSWORD"],
];

function requireText(value, name) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`${name} is required.`);
  }
  return value.trim();
}

function normalizeEnvironmentValue(value) {
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function requireAsciiPath(value, name) {
  const path = requireText(value, name);
  if (!/^[\x20-\x7e]+$/.test(path)) {
    throw new Error(`${name} must be ASCII-only.`);
  }
  return path;
}

export function resolveAndroidPublishStagingDirectory(environment = process.env) {
  return requireAsciiPath(
    normalizeEnvironmentValue(environment.CATALOG_ANDROID_PUBLISH_STAGING_DIR) ?? DEFAULT_STAGING_DIRECTORY,
    "Android publish staging directory",
  );
}

export function resolveAndroidSdkRoot(environment = process.env) {
  for (const name of ["ANDROID_SDK_ROOT", "ANDROID_HOME"]) {
    const value = normalizeEnvironmentValue(environment[name]);
    if (!value) {
      continue;
    }
    if (!isAbsolute(value)) {
      throw new Error(`${name} must be an absolute path.`);
    }
    return value;
  }
  return DEFAULT_ANDROID_SDK_ROOT;
}

export async function ensureAndroidSdkLocalProperties(androidProject, sdkRoot) {
  const nativeProject = requireText(androidProject, "Android native project path");
  const resolvedSdkRoot = requireText(sdkRoot, "Android SDK root");
  const localPropertiesPath = join(nativeProject, "local.properties");
  const expectedContent = `sdk.dir=${resolvedSdkRoot.replaceAll("\\", "/")}\n`;
  let currentContent = "";
  try {
    currentContent = await readFile(localPropertiesPath, "utf8");
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }
  if (currentContent.replaceAll("\r\n", "\n") !== expectedContent) {
    await writeFile(localPropertiesPath, expectedContent, "utf8");
  }
}

function validateExternalStagingDirectory(stagingDirectory, repositoryRoot) {
  const pathFromRepository = relative(repositoryRoot, stagingDirectory);
  if (
    !pathFromRepository
    || (!pathFromRepository.startsWith(`..${sep}`) && pathFromRepository !== ".." && !isAbsolute(pathFromRepository))
  ) {
    throw new Error("Android publish staging directory must be outside the repository.");
  }
}

export async function withStagedCandidateApk(candidateApk, stagingDirectory, inspect) {
  requireText(candidateApk, "Candidate APK path");
  requireAsciiPath(stagingDirectory, "Android publish staging directory");
  if (typeof inspect !== "function") {
    throw new Error("APK staging inspection callback is required.");
  }

  await mkdir(stagingDirectory, { recursive: true });
  const stagedApk = join(stagingDirectory, `candidate-${randomUUID()}.apk`);
  try {
    await copyFile(candidateApk, stagedApk);
    return await inspect(stagedApk);
  } finally {
    await rm(stagedApk, { force: true });
  }
}

export function readReleaseSigningContract(environment = process.env) {
  const values = Object.fromEntries(
    SIGNING_FIELDS.map(([field, variable]) => [field, normalizeEnvironmentValue(environment[variable])]),
  );
  const configuredCount = Object.values(values).filter(Boolean).length;
  if (configuredCount === SIGNING_FIELDS.length) {
    return values;
  }
  if (configuredCount > 0) {
    throw new Error("Android release signing configuration is incomplete. Configure every CATALOG_RELEASE_* value.");
  }

  const propertiesFile = normalizeEnvironmentValue(environment.CATALOG_RELEASE_PROPERTIES_FILE);
  if (propertiesFile) {
    return { propertiesFile };
  }

  throw new Error("Android release signing configuration is missing. Configure CATALOG_RELEASE_PROPERTIES_FILE or every CATALOG_RELEASE_* value.");
}

export function parseApkBadging(output) {
  const packageName = output.match(/package:\s+name='([^']+)'/)?.[1];
  const versionCode = Number(output.match(/versionCode='(\d+)'/)?.[1]);
  const versionName = output.match(/versionName='([^']+)'/)?.[1];
  if (!packageName) {
    throw new Error("aapt output does not contain an APK package name.");
  }
  if (!Number.isSafeInteger(versionCode) || versionCode <= 0) {
    throw new Error("aapt output does not contain a valid APK versionCode.");
  }
  if (!versionName) {
    throw new Error("aapt output does not contain an APK versionName.");
  }
  return { packageName, versionCode, versionName };
}

export function parseSignerFingerprint(output) {
  const rawFingerprint = output.match(/certificate SHA-256 digest:\s*([0-9a-fA-F:\s]+)/)?.[1];
  const fingerprint = rawFingerprint?.replace(/[^0-9a-fA-F]/g, "").toLowerCase();
  if (!fingerprint) {
    throw new Error("apksigner output does not contain a SHA-256 signing certificate fingerprint.");
  }
  return fingerprint;
}

export function validateReleaseArtifact(artifact, expectedRelease) {
  const packageName = requireText(artifact.packageName, "APK package name");
  const versionName = requireText(artifact.versionName, "APK versionName");
  const sha256 = requireText(artifact.sha256, "APK SHA-256").toLowerCase();
  const certificateSha256 = requireText(artifact.certificateSha256, "APK certificate SHA-256").toLowerCase();
  if (packageName !== expectedRelease.packageName) {
    throw new Error(`APK package name ${packageName} does not match ${expectedRelease.packageName}.`);
  }
  if (artifact.versionCode !== expectedRelease.versionCode || versionName !== expectedRelease.versionName) {
    throw new Error("APK version does not match version.json.");
  }
  if (!Number.isSafeInteger(artifact.sizeBytes) || artifact.sizeBytes <= 0) {
    throw new Error("APK size must be a positive integer.");
  }
  if (!/^[0-9a-f]{64}$/.test(sha256)) {
    throw new Error("APK SHA-256 must be a 64-character hexadecimal digest.");
  }
  if (!/^[0-9a-f]{64}$/.test(certificateSha256)) {
    throw new Error("APK certificate SHA-256 must be a 64-character hexadecimal fingerprint.");
  }
  return { ...artifact, sha256, certificateSha256 };
}

export function buildReleaseManifest(artifact, release) {
  const verified = validateReleaseArtifact(artifact, release);
  return {
    packageName: verified.packageName,
    versionCode: verified.versionCode,
    versionName: verified.versionName,
    apkPath: APK_PATH,
    sizeBytes: verified.sizeBytes,
    sha256: verified.sha256,
    certificateSha256: verified.certificateSha256,
    releaseNotes: release.releaseNotes,
    minimumSupportedVersionCode: release.minimumSupportedVersionCode,
  };
}

async function sha256File(filePath) {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(filePath)) {
    hash.update(chunk);
  }
  return hash.digest("hex");
}

function quoteCmdArgument(value) {
  const argument = requireText(value, "Windows command argument");
  if (/[\0\r\n"]/.test(argument)) {
    throw new Error("Windows command arguments cannot contain quotes or control characters.");
  }
  return `"${argument}"`;
}

export function createCommandInvocation(
  command,
  argumentsList,
  { platform = process.platform, commandInterpreter = process.env.ComSpec || "cmd.exe" } = {},
) {
  const executable = requireText(command, "Command");
  if (!Array.isArray(argumentsList)) {
    throw new Error("Command arguments must be an array.");
  }

  const spawnOptions = { shell: false, windowsHide: true };
  if (platform !== "win32" || !/\.(?:bat|cmd)$/i.test(executable)) {
    return { command: executable, argumentsList, spawnOptions };
  }

  const commandLine = [executable, ...argumentsList].map(quoteCmdArgument).join(" ");
  return {
    command: requireText(commandInterpreter, "Windows command interpreter"),
    argumentsList: ["/d", "/s", "/c", `call ${commandLine}`],
    spawnOptions: { ...spawnOptions, windowsVerbatimArguments: true },
  };
}

function run(command, argumentsList, options = {}) {
  const invocation = createCommandInvocation(command, argumentsList);
  return new Promise((resolve, reject) => {
    const child = spawn(invocation.command, invocation.argumentsList, {
      ...options,
      ...invocation.spawnOptions,
    });
    const stdout = [];
    const stderr = [];
    child.stdout.on("data", (chunk) => stdout.push(chunk));
    child.stderr.on("data", (chunk) => stderr.push(chunk));
    child.once("error", reject);
    child.once("close", (code) => {
      const output = Buffer.concat(stdout).toString("utf8");
      const errorOutput = Buffer.concat(stderr).toString("utf8");
      if (code === 0) {
        resolve({ output, errorOutput });
      } else {
        reject(new Error(`${command} failed with exit code ${code}.\n${errorOutput || output}`));
      }
    });
  });
}

async function resolveBuildTool(buildToolsDirectory, names) {
  for (const name of names) {
    const candidate = join(buildToolsDirectory, name);
    try {
      await access(candidate);
      return candidate;
    } catch {
      // Try the next platform-specific executable name.
    }
  }
  throw new Error(`Android build-tools is missing ${names.join(" or ")}.`);
}

async function findAndroidBuildTools(sdkRoot = resolveAndroidSdkRoot()) {
  const buildToolsRoot = join(sdkRoot, "build-tools");
  const entries = await readdir(buildToolsRoot, { withFileTypes: true });
  const versions = entries
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort((left, right) => right.localeCompare(left, undefined, { numeric: true }));
  if (versions.length === 0) {
    throw new Error(`No Android build-tools versions are installed under ${buildToolsRoot}.`);
  }

  const buildToolsDirectory = join(buildToolsRoot, versions[0]);
  return {
    apksigner: await resolveBuildTool(buildToolsDirectory, ["apksigner.bat", "apksigner"]),
    zipalign: await resolveBuildTool(buildToolsDirectory, ["zipalign.exe", "zipalign"]),
    aapt: await resolveBuildTool(buildToolsDirectory, ["aapt.exe", "aapt"]),
  };
}

async function inspectReleaseApk(apkPath, tools) {
  await run(tools.zipalign, ["-c", "-v", "4", apkPath]);
  const signature = await run(tools.apksigner, ["verify", "--verbose", "--print-certs", apkPath]);
  const badging = await run(tools.aapt, ["dump", "badging", apkPath]);
  const metadata = parseApkBadging(badging.output);
  return {
    ...metadata,
    sizeBytes: (await stat(apkPath)).size,
    sha256: await sha256File(apkPath),
    certificateSha256: parseSignerFingerprint(`${signature.output}\n${signature.errorOutput}`),
  };
}

export async function publishReleaseAtomically({ candidateApk, hostedApk, hostedManifest, manifest }) {
  const apkDirectory = dirname(hostedApk);
  const manifestDirectory = dirname(hostedManifest);
  if (apkDirectory !== manifestDirectory) {
    throw new Error("Hosted APK and manifest must be in the same directory for atomic publication.");
  }

  await mkdir(apkDirectory, { recursive: true });
  const temporaryApk = join(apkDirectory, `.${randomUUID()}.apk.tmp`);
  const temporaryManifest = join(apkDirectory, `.${randomUUID()}.json.tmp`);
  try {
    await copyFile(candidateApk, temporaryApk);
    await writeFile(temporaryManifest, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    await rename(temporaryApk, hostedApk);
    await rename(temporaryManifest, hostedManifest);
  } finally {
    await Promise.all([
      rm(temporaryApk, { force: true }),
      rm(temporaryManifest, { force: true }),
    ]);
  }
}

async function readExpectedRelease(repositoryRoot) {
  const [versionText, hostedManifestText] = await Promise.all([
    readFile(join(repositoryRoot, "version.json"), "utf8"),
    readFile(join(repositoryRoot, "发布包", "Android", "app-update.json"), "utf8"),
  ]);
  const version = JSON.parse(versionText);
  const hostedManifest = JSON.parse(hostedManifestText);
  const versionCode = version.android?.code;
  const versionName = version.product?.version;
  const minimumSupportedVersionCode = hostedManifest.minimumSupportedVersionCode;
  if (!Number.isSafeInteger(versionCode) || versionCode <= 0 || typeof versionName !== "string" || !versionName.trim()) {
    throw new Error("version.json must contain a valid Android version code and product version.");
  }
  if (!Number.isSafeInteger(minimumSupportedVersionCode) || minimumSupportedVersionCode <= 0 || minimumSupportedVersionCode > versionCode) {
    throw new Error("Hosted Android manifest has an invalid minimumSupportedVersionCode.");
  }
  return {
    packageName: PACKAGE_NAME,
    versionCode,
    versionName: versionName.trim(),
    releaseNotes: typeof hostedManifest.releaseNotes === "string" ? hostedManifest.releaseNotes : "",
    minimumSupportedVersionCode,
  };
}

async function validateSigningSource(signing) {
  if ("propertiesFile" in signing) {
    await access(signing.propertiesFile);
    return;
  }
  await access(signing.storeFile);
}

export function resolveNpmInvocation({ platform = process.platform, nodeExecutable = process.execPath } = {}) {
  if (platform !== "win32") {
    return { command: "npm", prefixArguments: [] };
  }

  const nodePath = requireText(nodeExecutable, "Node executable path");
  return {
    command: nodePath,
    prefixArguments: [join(dirname(nodePath), "node_modules", "npm", "bin", "npm-cli.js")],
  };
}

async function main() {
  const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
  const signing = readReleaseSigningContract();
  await validateSigningSource(signing);

  const androidRoot = join(repositoryRoot, "Android");
  const nativeProject = join(androidRoot, "android");
  const androidSdkRoot = resolveAndroidSdkRoot();
  const npm = resolveNpmInvocation();
  const gradle = process.platform === "win32" ? join(nativeProject, "gradlew.bat") : "./gradlew";
  await ensureAndroidSdkLocalProperties(nativeProject, androidSdkRoot);
  await run(npm.command, [...npm.prefixArguments, "run", "build"], { cwd: androidRoot });
  await run(npm.command, [...npm.prefixArguments, "run", "android:sync"], { cwd: androidRoot });
  await run(gradle, ["assembleRelease", "--no-daemon"], { cwd: nativeProject });

  const candidateApk = join(nativeProject, "app", "build", "outputs", "apk", "release", "app-release.apk");
  await access(candidateApk);
  const [tools, release] = await Promise.all([findAndroidBuildTools(androidSdkRoot), readExpectedRelease(repositoryRoot)]);
  const stagingDirectory = resolveAndroidPublishStagingDirectory();
  validateExternalStagingDirectory(stagingDirectory, repositoryRoot);
  const hostedDirectory = join(repositoryRoot, "发布包", "Android");
  const artifact = await withStagedCandidateApk(candidateApk, stagingDirectory, async (stagedApk) => {
    const inspected = validateReleaseArtifact(await inspectReleaseApk(stagedApk, tools), release);
    const manifest = buildReleaseManifest(inspected, release);
    await publishReleaseAtomically({
      candidateApk: stagedApk,
      hostedApk: join(hostedDirectory, "app-release.apk"),
      hostedManifest: join(hostedDirectory, "app-update.json"),
      manifest,
    });
    return inspected;
  });

  console.log(`Published signed Android release ${artifact.versionName} (${artifact.versionCode}).`);
  console.log(`APK size: ${artifact.sizeBytes} bytes`);
  console.log(`APK SHA-256: ${artifact.sha256}`);
  console.log(`Signing certificate SHA-256: ${artifact.certificateSha256}`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main().catch((error) => {
    console.error(`Android release publication failed: ${error.message}`);
    process.exitCode = 1;
  });
}
