import assert from "node:assert/strict";
import { test } from "node:test";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import {
  expectedGeneratedFiles,
  readVersionFile,
  validateRepositoryVersioning,
  validateVersionTargets,
} from "./version.mjs";

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const version = {
  product: { version: "1.1" },
  android: { code: 4 },
  taxonomy: { version: "1" },
};

test("version source defines the 1.1 product baseline", async () => {
  const actual = await readVersionFile(join(repositoryRoot, "version.json"));

  assert.deepEqual(actual, version);
});

test("generated version artifacts use every value from the source", () => {
  const files = expectedGeneratedFiles(version);

  assert.match(files["ComponentCatalogWinUI/Version.props"], /<Version>1\.1\.0<\/Version>/);
  assert.match(files["ComponentCatalogCore/Version.generated.cs"], /ProductVersionName = "1\.1"/);
  assert.match(files["ComponentCatalogCore/Version.generated.cs"], /AndroidVersionCode = 4/);
  assert.match(files["Android/src/version.generated.ts"], /PRODUCT_VERSION = "1\.1"/);
  assert.match(files["shared/version.generated.ts"], /TAXONOMY_VERSION = "1"/);
});

test("version validation reports metadata drift", () => {
  const targets = {
    ...expectedGeneratedFiles(version),
    "ComponentCatalogWinUI/ComponentCatalogWinUI.csproj": '<Project><Import Project="Version.props" /></Project>',
    "ComponentCatalogCore/Taxonomy/ComponentCategoryTaxonomy.cs": "ProductVersion.TaxonomyVersion",
    "ComponentCatalogWinUI/Services/CatalogStoragePaths.cs": "ProductVersion.AndroidVersionCode ProductVersion.ProductVersionName",
    "Android/android/app/build.gradle": "new JsonSlurper().parse(file('../../../version.json')) versionCode productVersion.android.code versionName productVersion.product.version",
    "Android/package.json": JSON.stringify({ version: "1.1.0" }),
    "Android/src/app-update.ts": 'import { ANDROID_VERSION_CODE, PRODUCT_VERSION } from "./version.generated";',
    "Android/src/snapshot-store.ts": 'import { TAXONOMY_VERSION } from "./version.generated";',
    "shared/catalog.ts": 'import { TAXONOMY_VERSION } from "./version.generated";',
    "Android/src/components/AboutView.tsx": 'import { CURRENT_APP_VERSION_NAME } from "../app-update";',
    "ComponentCatalogWinUI/Pages/AboutPage.xaml.cs": "Assembly.GetExecutingAssembly().GetName().Version",
    "发布包/Android/app-update.json": JSON.stringify({ versionCode: 4, versionName: "1.1" }),
  };

  assert.deepEqual(validateVersionTargets(version, targets), []);

  const driftedManifest = {
    ...targets,
    "发布包/Android/app-update.json": JSON.stringify({ versionCode: 3, versionName: "1.1" }),
  };
  assert.match(validateVersionTargets(version, driftedManifest).join("\n"), /app-update\.json versionCode/);

  const driftedPackage = {
    ...targets,
    "Android/package.json": JSON.stringify({ version: "1.2.0" }),
  };
  assert.match(validateVersionTargets(version, driftedPackage).join("\n"), /Android\/package\.json version/);
});

test("repository metadata has no version drift", async () => {
  assert.deepEqual(await validateRepositoryVersioning(repositoryRoot), []);
});
