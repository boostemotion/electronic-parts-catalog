import { registerPlugin } from "@capacitor/core";

import type { AvailableAppUpdate } from "./app-update";

interface NativeAppUpdateBridge {
  downloadAndInstall(options: { apkUrl: string; sha256: string; sizeBytes: number }): Promise<void>;
}

const nativeAppUpdateBridge = registerPlugin<NativeAppUpdateBridge>("AppUpdate");

export function downloadAndInstallUpdate(update: AvailableAppUpdate): Promise<void> {
  return nativeAppUpdateBridge.downloadAndInstall({
    apkUrl: update.apkUrl,
    sha256: update.manifest.sha256,
    sizeBytes: update.manifest.sizeBytes,
  });
}
