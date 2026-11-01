import { readFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

const appCss = readFileSync(path.resolve(__dirname, "../App.css"), "utf8");

describe("Android scroll layout contract", () => {
  it("keeps the main content area scrollable inside the app shell", () => {
    expect(appCss).toMatch(/\.android-app-container\s*\{[^}]*height:\s*100vh;/s);
    expect(appCss).toMatch(/\.android-app-container\s*\{[^}]*box-sizing:\s*border-box;/s);
    expect(appCss).toMatch(/\.android-content-area\s*\{[^}]*min-height:\s*0;/s);
    expect(appCss).toMatch(/\.android-content-area\s*\{[^}]*overflow-y:\s*auto;/s);
    expect(appCss).toMatch(/\.android-content-area\s*\{[^}]*-webkit-overflow-scrolling:\s*touch;/s);
  });

  it("adds a soft immersive backdrop behind the transparent status bar", () => {
    expect(appCss).toMatch(/\.android-app-container\s*\{[^}]*position:\s*relative;/s);
    expect(appCss).toMatch(/\.android-app-container\s*\{[^}]*padding-top:\s*calc\(env\(safe-area-inset-top,\s*0px\)\s*\+\s*18px\);/s);
    expect(appCss).toMatch(/\.android-app-container::before\s*\{[^}]*position:\s*fixed;/s);
    expect(appCss).toMatch(/\.android-app-container::before\s*\{[^}]*height:\s*calc\(env\(safe-area-inset-top,\s*0px\)\s*\+\s*56px\);/s);
    expect(appCss).toMatch(/\.android-app-container::before\s*\{[^}]*backdrop-filter:\s*blur\(/s);
    expect(appCss).toMatch(/\.android-app-container::before\s*\{[^}]*pointer-events:\s*none;/s);
  });

  it("keeps dashboard stats as a compact three-column strip on mobile", () => {
    expect(appCss).toMatch(/\.md3-summary-grid\s*\{[^}]*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\);/s);
    expect(appCss).toMatch(/\.md3-summary-card\s*\{[^}]*padding:\s*10px\s+8px;/s);
    expect(appCss).toMatch(/\.md3-summary-card\s*\{[^}]*gap:\s*4px;/s);
    expect(appCss).toMatch(/\.md3-summary-card\s*\{[^}]*box-shadow:\s*none;/s);
    expect(appCss).not.toMatch(/@media\s*\(max-width:\s*640px\)\s*\{[\s\S]*?\.md3-summary-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr;/s);
  });

  it("uses a tighter top bar and smaller action button for the tool-style shell", () => {
    expect(appCss).toMatch(/\.md3-top-app-bar\s*\{[^}]*display:\s*contents;/s);
    expect(appCss).toMatch(/\.md3-top-app-bar__row\s*\{[^}]*align-items:\s*center;/s);
    expect(appCss).toMatch(/\.md3-top-app-bar__row\s*\{[^}]*padding:\s*0\s+4px\s+2px;/s);
    expect(appCss).toMatch(/\.md3-top-app-bar__row\s*\{[^}]*min-height:\s*40px;/s);
    expect(appCss).toMatch(/\.md3-icon-pill\s*\{[^}]*width:\s*36px;/s);
    expect(appCss).toMatch(/\.md3-icon-pill\s*\{[^}]*height:\s*36px;/s);
    expect(appCss).toMatch(/\.md3-search-bar-with-actions\s*\{[^}]*padding-right:\s*148px;/s);
    expect(appCss).not.toMatch(/@media\s*\(max-width:\s*640px\)\s*\{[\s\S]*?\.md3-top-app-bar__row[\s\S]*?flex-direction:\s*column;/s);
    expect(appCss).toMatch(/\.android-content-area\s*\{[^}]*padding:\s*12px\s+16px\s+calc\(104px\s*\+\s*env\(safe-area-inset-bottom,\s*0px\)\);/s);
    expect(appCss).toMatch(/@media\s*\(max-width:\s*640px\)\s*\{[\s\S]*?\.android-content-area\s*\{[\s\S]*?padding:\s*8px\s+14px\s+calc\(104px\s*\+\s*env\(safe-area-inset-bottom,\s*0px\)\);/s);
  });

  it("keeps sync dashboard metrics and diagnostics compact and separated", () => {
    expect(appCss).toMatch(/\.md3-sync-dashboard,\s*\.md3-about-dashboard\s*\{[^}]*display:\s*flex;/s);
    expect(appCss).toMatch(/\.md3-sync-dashboard \.md3-dashboard-metrics\s*\{[^}]*grid-template-columns:\s*repeat\(3,\s*1fr\);/s);
    expect(appCss).toMatch(/\.sync-diagnostic-row,[\s\S]*?\{[^}]*grid-template-columns:\s*72px minmax\(0,\s*1fr\);/s);
    expect(appCss).toMatch(/\.sync-diagnostic-value,[\s\S]*?\{[^}]*overflow-wrap:\s*anywhere;/s);
    expect(appCss).toMatch(/\.sync-status-notice\s*\{[^}]*color:\s*var\(--md-sys-color-error\);/s);
  });
});
