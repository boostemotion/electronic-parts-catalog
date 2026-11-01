import { describe, expect, it } from "vitest";

import { sortSearchResults } from "../catalog-sort";
import type { ComponentRecord, SearchResult } from "@shared/types";

function component(id: string, name: string, partNumber: string, category = "其他"): ComponentRecord {
  return {
    id,
    name,
    partNumber,
    category,
    package: "0603",
    aliases: [],
    keywords: [],
    attributes: {},
    owned: true,
    location: "",
    substitutes: [],
    notes: "",
    sourceType: "manual",
  };
}

function result(value: ComponentRecord, score = 1): SearchResult {
  return { component: value, score, matchedFields: [] };
}

describe("catalog sorting", () => {
  const alpha = result(component("a", "稳压器", "ZZZ-001", "电源"));
  const beta = result(component("b", "电阻", "AAA-001", "无源"));
  const gamma = result(component("c", "电容", "MMM-001", "无源"));

  it("keeps the relevance ordering when requested", () => {
    expect(sortSearchResults([alpha, beta], "relevance")).toEqual([alpha, beta]);
  });

  it("sorts by part number and name with Chinese locale comparison", () => {
    expect(sortSearchResults([alpha, beta, gamma], "partNumber").map((item) => item.component.id)).toEqual([
      "b",
      "c",
      "a",
    ]);
    expect(sortSearchResults([alpha, beta, gamma], "name").map((item) => item.component.id)).toEqual([
      "c",
      "b",
      "a",
    ]);
  });

  it("sorts by category and falls back to name within the same category", () => {
    expect(sortSearchResults([alpha, beta, gamma], "category").map((item) => item.component.id)).toEqual([
      "a",
      "c",
      "b",
    ]);
  });
});
