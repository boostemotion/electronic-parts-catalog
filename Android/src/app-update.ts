import { nativeHttpBridge, type DiscoveredCatalog, type HttpBridge } from "./sync-client";
import { ANDROID_VERSION_CODE, PRODUCT_VERSION } from "./version.generated";

export const CURRENT_APP_VERSION_CODE = ANDROID_VERSION_CODE;
export const CURRENT_APP_VERSION_NAME = PRODUCT_VERSION;
export const APP_UPDATE_MANIFEST_PATH = "/mobile/app-update";
export const APP_UPDATE_APK_PATH = "/mobile/app.apk";
const EXPECTED_PACKAGE_NAME = "com.partshelf.catalog";

export interface AppUpdateManifest {
  packageName: string;
  versionCode: number;
  versionName: string;
  apkPath: string;
  sizeBytes: number;
  sha256: string;
  releaseNotes?: string;
  minimumSupportedVersionCode: number;
}

export interface AvailableAppUpdate {
  manifest: AppUpdateManifest;
  apkUrl: string;
}

export type UpdateHttpBridge = HttpBridge;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isPositiveSafeInteger(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0;
}

export function validateAppUpdateManifest(value: unknown): AppUpdateManifest | null {
  if (!isRecord(value)) {
    return null;
  }

  const { packageName, versionCode, versionName, apkPath, sizeBytes, sha256, releaseNotes, minimumSupportedVersionCode } = value;
  if (
    packageName !== EXPECTED_PACKAGE_NAME ||
    !isPositiveSafeInteger(versionCode) ||
    typeof versionName !== "string" ||
    !versionName.trim() ||
    apkPath !== APP_UPDATE_APK_PATH ||
    !isPositiveSafeInteger(sizeBytes) ||
    typeof sha256 !== "string" ||
    !/^[a-f0-9]{64}$/i.test(sha256) ||
    (releaseNotes !== undefined && (typeof releaseNotes !== "string" || releaseNotes.length > 500)) ||
    !isPositiveSafeInteger(minimumSupportedVersionCode) ||
    minimumSupportedVersionCode > versionCode
  ) {
    return null;
  }

  return {
    packageName,
    versionCode,
    versionName: versionName.trim(),
    apkPath,
    sizeBytes,
    sha256: sha256.toLowerCase(),
    ...(releaseNotes ? { releaseNotes } : {}),
    minimumSupportedVersionCode,
  };
}

function getUpdateUrls(catalog: DiscoveredCatalog): { manifestUrl: string; apkUrl: string } {
  const snapshotUrl = new URL(catalog.serviceUrl);
  if (snapshotUrl.pathname !== "/mobile/snapshot" || snapshotUrl.search || snapshotUrl.hash) {
    throw new Error("发现的 Windows 地址无效");
  }

  return {
    manifestUrl: new URL(APP_UPDATE_MANIFEST_PATH, snapshotUrl).toString(),
    apkUrl: new URL(APP_UPDATE_APK_PATH, snapshotUrl).toString(),
  };
}

export async function fetchAvailableAppUpdate(
  catalog: DiscoveredCatalog,
  currentVersionCode = CURRENT_APP_VERSION_CODE,
  bridge: UpdateHttpBridge = nativeHttpBridge,
): Promise<AvailableAppUpdate | null> {
  const { manifestUrl, apkUrl } = getUpdateUrls(catalog);
  const response = await bridge.get({
    url: manifestUrl,
    headers: { Accept: "application/json" },
    connectTimeout: 5000,
    readTimeout: 10000,
  });

  if (response.status === 404) {
    return null;
  }
  if (response.status < 200 || response.status >= 300) {
    throw new Error(`更新检查失败: ${response.status}`);
  }

  const manifestPayload = typeof response.data === "string" ? JSON.parse(response.data) as unknown : response.data;
  const manifest = validateAppUpdateManifest(manifestPayload);
  if (!manifest) {
    throw new Error("更新清单结构无效");
  }
  if (manifest.versionCode <= currentVersionCode) {
    return null;
  }

  return { manifest, apkUrl };
}
