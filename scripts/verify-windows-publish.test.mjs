import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

test("Windows publish verification isolates catalog data from the live workspace", async () => {
  const script = await readFile(join(repositoryRoot, "发布包", "Verify-WindowsPublish.ps1"), "utf8");

  assert.match(script, /\$verificationCatalogRoot = Join-Path \$publishDirectory '\.catalog-data'/);
  assert.match(script, /SetEnvironmentVariable\('COMPONENT_CATALOG_ROOT', \$verificationCatalogRoot, 'Process'\)/);
  assert.match(script, /SetEnvironmentVariable\('COMPONENT_CATALOG_ROOT', \$originalCatalogRoot, 'Process'\)/);
});
