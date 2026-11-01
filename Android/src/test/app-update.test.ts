import { describe, expect, it, vi } from "vitest";

import {
  CURRENT_APP_VERSION_CODE,
  fetchAvailableAppUpdate,
  validateAppUpdateManifest,
  type AppUpdateManifest,
  type UpdateHttpBridge,
} from "../app-update";

const validManifest: AppUpdateManifest = {
  packageName: "com.partshelf.catalog",
  versionCode: 2,
  versionName: "1.1",
  apkPath: "/mobile/app.apk",
  sizeBytes: 4_495_763,
  sha256: "a".repeat(64),
  releaseNotes: "筛选界面优化与自动更新",
  minimumSupportedVersionCode: 1,
};

describe("Android app update contract", () => {
  it("accepts the exact fixed-path manifest and rejects malformed or redirected update metadata", () => {
    expect(validateAppUpdateManifest(validManifest)).toEqual(validManifest);
    expect(validateAppUpdateManifest({ ...validManifest, apkPath: "/mobile/other.apk" })).toBeNull();
    expect(validateAppUpdateManifest({ ...validManifest, sha256: "bad" })).toBeNull();
    expect(validateAppUpdateManifest({ ...validManifest, versionCode: 0 })).toBeNull();
    expect(validateAppUpdateManifest({ ...validManifest, minimumSupportedVersionCode: 3 })).toBeNull();
  });

  it("returns an update only when the discovered host serves a newer version", async () => {
    const nativeGet = vi.fn().mockResolvedValue({ data: validManifest, status: 200, headers: {}, url: "" });
    const bridge: UpdateHttpBridge = { get: nativeGet };

    await expect(
      fetchAvailableAppUpdate(
        { serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "电子元件入库台" },
        1,
        bridge,
      ),
    ).resolves.toEqual({
      manifest: validManifest,
      apkUrl: "http://192.168.1.8:10849/mobile/app.apk",
    });
    expect(nativeGet).toHaveBeenCalledWith({
      url: "http://192.168.1.8:10849/mobile/app-update",
      headers: { Accept: "application/json" },
      connectTimeout: 5000,
      readTimeout: 10000,
    });
  });

  it("does not nag when no release exists or the current app is already current", async () => {
    const missingBridge: UpdateHttpBridge = { get: vi.fn().mockResolvedValue({ data: null, status: 404, headers: {}, url: "" }) };
    await expect(
      fetchAvailableAppUpdate({ serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "" }, 1, missingBridge),
    ).resolves.toBeNull();

    const currentBridge: UpdateHttpBridge = { get: vi.fn().mockResolvedValue({ data: validManifest, status: 200, headers: {}, url: "" }) };
    await expect(
      fetchAvailableAppUpdate({ serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "" }, CURRENT_APP_VERSION_CODE, currentBridge),
    ).resolves.toBeNull();
  });
});
