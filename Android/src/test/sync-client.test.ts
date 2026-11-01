import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  discoverWindowsCatalog,
  fetchSnapshotFromPc,
  isPrivateLanIpv4,
  restoreDiscoveredCatalog,
  validateDiscoveryResponse,
  type DiscoveryBridge,
  type HttpBridge,
} from "../sync-client";

const snapshot = {
  meta: {
    exportedAt: "2026-07-12T00:00:00.000Z",
    componentCount: 1,
    categories: ["线性稳压器(LDO)"],
    taxonomyVersion: "1",
    majorCategories: [
      {
        id: "protection-power",
        label: "板级电路保护 / 电源管理",
        order: 1,
        subcategories: [{ id: "ldo", majorCategoryId: "protection-power", label: "线性稳压器(LDO)", order: 1 }],
      },
    ],
  },
  components: [
    {
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
    },
  ],
};

function bridgeReturning(responses: unknown[]): DiscoveryBridge {
  return { discover: vi.fn().mockResolvedValue({ responses }) };
}

describe("mobile discovery client", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("accepts only the V1 read-only snapshot response on a private IPv4 host", () => {
    expect(
      validateDiscoveryResponse({
        service: "component-catalog",
        version: 1,
        name: "电子元件入库台",
        host: "192.168.1.8",
        port: 10849,
        path: "/mobile/snapshot",
        readOnly: true,
      }),
    ).toEqual({
      serviceUrl: "http://192.168.1.8:10849/mobile/snapshot",
      name: "电子元件入库台",
    });
  });

  it.each([
    { service: "other", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 2, host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 1, host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 1, name: "另一台目录", host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 1, name: "电子元件入库台", host: "8.8.8.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 0, path: "/mobile/snapshot", readOnly: true },
    { service: "component-catalog", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 10849, path: "/components", readOnly: true },
    { service: "component-catalog", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: false },
  ])("rejects an invalid discovery response", (response) => {
    expect(validateDiscoveryResponse(response)).toBeNull();
  });

  it("deduplicates valid native responses and ignores malformed payloads", async () => {
    const bridge = bridgeReturning([
      "not json",
      { service: "component-catalog", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
      { service: "component-catalog", version: 1, name: "电子元件入库台", host: "192.168.1.8", port: 10849, path: "/mobile/snapshot", readOnly: true },
      { service: "component-catalog", version: 1, name: "电子元件入库台", host: "127.0.0.1", port: 10849, path: "/mobile/snapshot", readOnly: true },
    ]);

    await expect(discoverWindowsCatalog(bridge)).resolves.toEqual([
      { serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "电子元件入库台" },
    ]);
    expect(bridge.discover).toHaveBeenCalledWith({ timeoutMs: 1800 });
  });

  it("uses the native Capacitor HTTP bridge for a GET-only snapshot request without Authorization", async () => {
    const nativeGet = vi.fn().mockResolvedValue({
      data: snapshot,
      status: 200,
      headers: {},
      url: "http://192.168.1.8:10849/mobile/snapshot",
    });
    const bridge: HttpBridge = { get: nativeGet };

    await expect(fetchSnapshotFromPc({ serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "" }, bridge)).resolves.toEqual(snapshot);

    expect(nativeGet).toHaveBeenCalledWith({
      url: "http://192.168.1.8:10849/mobile/snapshot",
      headers: { Accept: "application/json" },
      connectTimeout: 5000,
      readTimeout: 10000,
    });
    expect(nativeGet.mock.calls[0]?.[0].headers).not.toHaveProperty("Authorization");
  });

  it("rejects invalid snapshots before they can replace the offline catalog", async () => {
    const bridge: HttpBridge = { get: vi.fn().mockResolvedValue({
      data: { meta: {}, components: "bad" },
      status: 200,
      headers: {},
      url: "http://192.168.1.8:10849/mobile/snapshot",
    }) };

    await expect(fetchSnapshotFromPc({ serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "" }, bridge)).rejects.toThrow("目录快照结构无效");
  });

  it("reports the discovered Windows URL when the native HTTP bridge fails", async () => {
    const bridge: HttpBridge = { get: vi.fn().mockRejectedValue(new Error("connect timed out")) };

    await expect(fetchSnapshotFromPc({ serviceUrl: "http://192.168.1.8:10849/mobile/snapshot", name: "" }, bridge))
      .rejects.toThrow("192.168.1.8:10849");
  });

  it("recognizes private LAN IPv4 addresses but rejects loopback and public hosts", () => {
    expect(isPrivateLanIpv4("10.0.0.1")).toBe(true);
    expect(isPrivateLanIpv4("172.16.0.1")).toBe(true);
    expect(isPrivateLanIpv4("192.168.1.1")).toBe(true);
    expect(isPrivateLanIpv4("127.0.0.1")).toBe(false);
    expect(isPrivateLanIpv4("8.8.8.8")).toBe(false);
  });

  it("restores only a previously validated private read-only snapshot URL", () => {
    expect(restoreDiscoveredCatalog("http://192.168.1.8:10849/mobile/snapshot")).toEqual({
      serviceUrl: "http://192.168.1.8:10849/mobile/snapshot",
      name: "电子元件入库台",
    });
    expect(restoreDiscoveredCatalog("http://8.8.8.8:10849/mobile/snapshot")).toBeNull();
    expect(restoreDiscoveredCatalog("http://192.168.1.8:10849/components")).toBeNull();
    expect(restoreDiscoveredCatalog(null)).toBeNull();
  });
});
