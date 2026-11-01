import { describe, expect, it } from "vitest";

import { buildCatalogSnapshot, searchComponents } from "@shared/catalog";
import { normalizeSnapshot } from "../snapshot-store";
import type { ComponentRecord } from "@shared/types";

const taxonomy = [
  {
    id: "protection-power",
    label: "板级电路保护 / 电源管理",
    order: 1,
    subcategories: [
      {
        id: "ldo",
        majorCategoryId: "protection-power",
        label: "线性稳压器(LDO)",
        order: 1,
      },
      {
        id: "power-other",
        majorCategoryId: "protection-power",
        label: "其他电源管理",
        order: 2,
      },
    ],
  },
];

const component: ComponentRecord = {
  id: "ldo-1",
  name: "3.3V 稳压器",
  partNumber: "AMS1117-3.3",
  category: "线性稳压器(LDO)",
  majorCategoryId: "protection-power",
  subCategoryId: "ldo",
  package: "SOT223",
  aliases: [],
  keywords: ["ldo"],
  attributes: { voltage: "3.3V" },
  owned: true,
  location: "A1",
  substitutes: [],
  notes: "",
  sourceType: "manual",
};

describe("snapshot contract", () => {
  it("preserves taxonomy v1 metadata when building a snapshot", () => {
    const snapshot = buildCatalogSnapshot([component], "2026-07-12T00:00:00.000Z", {
      taxonomyVersion: "1",
      majorCategories: taxonomy,
    });

    expect(snapshot).toMatchObject({
      meta: {
        taxonomyVersion: "1",
        majorCategories: taxonomy,
        componentCount: 1,
      },
      components: [component],
    });
  });

  it("filters with taxonomy IDs while retaining the legacy category display filter", () => {
    const differentSubcategory = { ...component, id: "power-other-1", subCategoryId: "power-other" };

    expect(
      searchComponents([component, differentSubcategory], {
        text: "",
        category: "全部",
        majorCategoryId: "protection-power",
        subCategoryId: "ldo",
      }),
    ).toEqual([expect.objectContaining({ component })]);
    expect(searchComponents([component], { text: "", category: "线性稳压器(LDO)" })).toHaveLength(1);
  });

  it("keeps a current taxonomy v1 snapshot unchanged", () => {
    const current = buildCatalogSnapshot([component], "2026-07-12T00:00:00.000Z", {
      taxonomyVersion: "1",
      majorCategories: taxonomy,
    });

    expect(normalizeSnapshot(current)).toEqual({ snapshot: current, status: "current" });
  });

  it("upgrades known legacy categories through the compatibility mapping", () => {
    const legacy = {
      meta: {
        exportedAt: "2026-07-12T00:00:00.000Z",
        componentCount: 1,
        categories: ["电源"],
      },
      components: [{ ...component, category: "电源", majorCategoryId: undefined, subCategoryId: undefined }],
    };

    const normalized = normalizeSnapshot(legacy);

    expect(normalized.status).toBe("upgraded");
    expect(normalized.snapshot.components[0]).toMatchObject({
      category: "电源",
      majorCategoryId: "protection-power",
      subCategoryId: "power-other",
    });
  });

  it("keeps ambiguous legacy records read-only without inventing taxonomy IDs", () => {
    const legacy = {
      meta: {
        exportedAt: "2026-07-12T00:00:00.000Z",
        componentCount: 1,
        categories: ["自定义分类"],
      },
      components: [{ ...component, category: "自定义分类", majorCategoryId: undefined, subCategoryId: undefined }],
    };

    const normalized = normalizeSnapshot(legacy);

    expect(normalized.status).toBe("legacy-read-only");
    expect(normalized.snapshot.components[0].majorCategoryId).toBeUndefined();
    expect(normalized.snapshot.components[0].subCategoryId).toBeUndefined();
  });

  it("marks unknown taxonomy IDs read-only rather than trusting or remapping them", () => {
    const legacy = buildCatalogSnapshot(
      [{ ...component, majorCategoryId: "unknown-major", subCategoryId: "unknown-sub" }],
      "2026-07-12T00:00:00.000Z",
      { taxonomyVersion: "1", majorCategories: taxonomy },
    );

    const normalized = normalizeSnapshot(legacy);

    expect(normalized.status).toBe("legacy-read-only");
    expect(normalized.snapshot.components[0].majorCategoryId).toBeUndefined();
    expect(normalized.snapshot.components[0].subCategoryId).toBeUndefined();
  });

  it("replaces malformed snapshots with an empty standard snapshot and explicit status", () => {
    const normalized = normalizeSnapshot({ meta: {}, components: "not-an-array" });

    expect(normalized.status).toBe("invalid");
    expect(normalized.snapshot).toEqual({
      meta: {
        exportedAt: "",
        componentCount: 0,
        categories: [],
        taxonomyVersion: "1",
        majorCategories: [],
      },
      components: [],
    });
  });
});
