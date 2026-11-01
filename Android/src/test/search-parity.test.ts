import { describe, expect, it } from "vitest";

import { searchComponents } from "@shared/catalog";
import type { ComponentRecord } from "@shared/types";

const component: ComponentRecord = {
  id: "parity-1",
  name: "NPN 三极管",
  partNumber: "S8050",
  category: "三极管(BJT)",
  majorCategoryId: "diodes-transistors",
  subCategoryId: "bjt",
  package: "SOT-23",
  aliases: [],
  keywords: [],
  attributes: {},
  owned: true,
  location: "",
  substitutes: [],
  notes: "",
  sourceType: "manual",
};

describe("shared search parity", () => {
  it("matches the package filter case-insensitively like the .NET core", () => {
    expect(searchComponents([component], { text: "", category: "全部", package: "sot-23" })).toHaveLength(1);
    expect(searchComponents([component], { text: "", category: "全部", package: "SOT-23" })).toHaveLength(1);
  });

  it("still rejects a different package value", () => {
    expect(searchComponents([component], { text: "", category: "全部", package: "sot-232" })).toHaveLength(0);
  });
});
