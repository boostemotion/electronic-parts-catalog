import { readFileSync } from "node:fs";
import path from "node:path";

import { describe, expect, it } from "vitest";

const srcDirectory = path.resolve(__dirname, "..");
const appCss = readFileSync(path.join(srcDirectory, "App.css"), "utf8");
const indexCss = readFileSync(path.join(srcDirectory, "index.css"), "utf8");

function collectVariables(css: string, pattern: RegExp): Set<string> {
  return new Set([...css.matchAll(pattern)].map((match) => match[1]));
}

describe("Material 3 style contract", () => {
  it("defines every custom property consumed by the stylesheets", () => {
    const used = collectVariables(`${appCss}\n${indexCss}`, /var\((--md-[a-z0-9-]+)/g);
    const defined = collectVariables(`${appCss}\n${indexCss}`, /(--md-[a-z0-9-]+)\s*:/g);
    const missing = [...used].filter((variable) => !defined.has(variable)).sort();

    expect(missing).toEqual([]);
  });

  it("routes page and card motion through motion.css with a bounded stagger", () => {
    const appSource = readFileSync(path.join(srcDirectory, "App.tsx"), "utf8");
    const catalogSource = readFileSync(path.join(srcDirectory, "components", "CatalogView.tsx"), "utf8");
    const motionCss = readFileSync(path.join(srcDirectory, "motion.css"), "utf8");

    expect(appSource).toContain('import "./motion.css";');
    expect(motionCss).toMatch(/\.md3-list > \.md3-catalog-card:nth-child\(n\+11\)\s*\{\s*animation-delay:\s*300ms/);
    expect(motionCss).toContain("prefers-reduced-motion: reduce");
    expect(catalogSource).not.toMatch(/index \* 36/);
  });
});
