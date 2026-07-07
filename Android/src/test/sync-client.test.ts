import { beforeEach, describe, expect, it, vi } from "vitest";

import { deleteComponentFromPc, fetchSnapshotFromPc, normalizePcAddress, upsertComponentToPc } from "../sync-client";
import type { ComponentRecord } from "@shared/types";

const component: ComponentRecord = {
  id: "ldo-1",
  name: "3.3V 稳压器",
  partNumber: "AMS1117-3.3",
  category: "电源",
  package: "SOT223",
  aliases: ["LDO 3v3"],
  keywords: ["ldo"],
  attributes: { voltage: "3.3V" },
  owned: true,
  location: "A1",
  substitutes: [],
  notes: "",
  sourceType: "manual",
};

describe("mobile sync client", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("normalizes LAN PC API addresses", () => {
    expect(normalizePcAddress("192.168.1.8:10849/")).toBe("http://192.168.1.8:10849");
    expect(normalizePcAddress(" http://192.168.1.8:10849// ")).toBe("http://192.168.1.8:10849");
  });

  it("fetches a snapshot and removes the desktop-only all category", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/components")) {
        return new Response(JSON.stringify({ results: [{ component }] }), { status: 200 });
      }

      if (url.endsWith("/categories")) {
        return new Response(JSON.stringify({ categories: ["全部", "电源"] }), { status: 200 });
      }

      return new Response("not found", { status: 404 });
    });
    vi.stubGlobal("fetch", fetchMock);

    const snapshot = await fetchSnapshotFromPc("192.168.1.8:10849");

    expect(snapshot.components).toHaveLength(1);
    expect(snapshot.components[0].partNumber).toBe("AMS1117-3.3");
    expect(snapshot.meta.categories).toEqual(["电源"]);
    expect(snapshot.meta.componentCount).toBe(1);
  });

  it("throws when desktop writes fail", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("bad", { status: 500 })));

    await expect(upsertComponentToPc("http://192.168.1.8:10849", component)).rejects.toThrow(
      "PC 写入失败",
    );
    await expect(deleteComponentFromPc("http://192.168.1.8:10849", component.id)).rejects.toThrow(
      "PC 删除失败",
    );
  });
});
