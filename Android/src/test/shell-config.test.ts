import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

const projectRoot = resolve(import.meta.dirname, "../..");

function readProjectFile(relativePath: string): string {
  return readFileSync(resolve(projectRoot, relativePath), "utf8");
}

describe("android shell resources", () => {
  it("declares a Chinese mobile web shell with a branded theme color", () => {
    const html = readProjectFile("index.html");

    expect(html).toContain('<html lang="zh-CN">');
    expect(html).toContain('name="theme-color"');
    expect(html).toContain('content="#0f6cbd"');
    expect(html).toContain('viewport-fit=cover');
  });

  it("uses branded Android launch resources and splash theme settings", () => {
    const strings = readProjectFile("android/app/src/main/res/values/strings.xml");
    const styles = readProjectFile("android/app/src/main/res/values/styles.xml");
    const launcherBackground = readProjectFile("android/app/src/main/res/drawable/ic_launcher_background.xml");
    const launcherForeground = readProjectFile("android/app/src/main/res/drawable-v24/ic_launcher_foreground.xml");

    expect(strings).toContain("<string name=\"app_name\">随手查料</string>");
    expect(styles).toContain("windowSplashScreenBackground");
    expect(styles).toContain("windowSplashScreenAnimatedIcon");
    expect(styles).toContain("postSplashScreenTheme");
    expect(launcherBackground).toContain("#0F6CBD");
    expect(launcherForeground).toContain("android:pathData");
  });

  it("replaces template web icons with the catalog brand mark", () => {
    const favicon = readProjectFile("public/favicon.svg");
    const iconSprite = readProjectFile("public/icons.svg");

    expect(favicon).toContain("tray-mark");
    expect(favicon).toContain("#0F6CBD");
    expect(iconSprite).toContain("symbol id=\"app-mark\"");
    expect(iconSprite).not.toContain("bluesky-icon");
  });

  it("configures an immersive Android activity shell with the status bar hidden", () => {
    const manifest = readProjectFile("android/app/src/main/AndroidManifest.xml");
    const styles = readProjectFile("android/app/src/main/res/values/styles.xml");
    const mainActivity = readProjectFile("android/app/src/main/java/com/partshelf/catalog/MainActivity.java");

    expect(manifest).toContain('android:windowSoftInputMode="adjustResize"');
    expect(styles).toContain("@android:color/transparent");
    expect(mainActivity).toContain("WindowCompat.setDecorFitsSystemWindows(getWindow(), false);");
    expect(mainActivity).toContain("WindowInsetsControllerCompat");
    expect(mainActivity).toContain("WindowInsetsCompat.Type.statusBars()");
    expect(mainActivity).toContain("controller.hide(");
    expect(mainActivity).toContain("BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE");
    expect(mainActivity).toContain("setAppearanceLightNavigationBars(true)");
  });

  it("declares the package installer permission required by in-app APK updates", () => {
    const manifest = readProjectFile("android/app/src/main/AndroidManifest.xml");

    expect(manifest).toContain("android.permission.REQUEST_INSTALL_PACKAGES");
  });
});
