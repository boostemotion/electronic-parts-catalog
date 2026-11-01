import { registerPlugin } from "@capacitor/core";
import type { ExportCatalogSnapshot } from "@shared/types";
import { normalizeSnapshot } from "./snapshot-store";

const DISCOVERY_TIMEOUT_MS = 1800;
const MOBILE_SNAPSHOT_PATH = "/mobile/snapshot";

export interface DiscoveredCatalog {
  serviceUrl: string;
  name: string;
}

export interface DiscoveryBridge {
  discover(options: { timeoutMs: number }): Promise<{ responses?: unknown[] }>;
}

export interface HttpBridge {
  get(options: {
    url: string;
    headers: Record<string, string>;
    connectTimeout: number;
    readTimeout: number;
  }): Promise<{ data: unknown; status: number; headers: Record<string, string>; url: string }>;
}

const nativeDiscoveryBridge = registerPlugin<DiscoveryBridge>("CatalogDiscovery");
export const nativeHttpBridge = registerPlugin<HttpBridge>("CapacitorHttp");

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function isPrivateLanIpv4(hostname: string): boolean {
  const octets = hostname.split(".").map(Number);
  if (octets.length !== 4 || octets.some((octet) => !Number.isInteger(octet) || octet < 0 || octet > 255)) {
    return false;
  }

  return (
    octets[0] === 10 ||
    (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) ||
    (octets[0] === 192 && octets[1] === 168)
  );
}

export function validateDiscoveryResponse(value: unknown): DiscoveredCatalog | null {
  const response = typeof value === "string" ? tryParseJson(value) : value;
  if (!isRecord(response)) {
    return null;
  }

  const { service, version, name, host, port, path, readOnly } = response;
  if (
    service !== "component-catalog" ||
    version !== 1 ||
    name !== "电子元件入库台" ||
    typeof host !== "string" ||
    !isPrivateLanIpv4(host) ||
    typeof port !== "number" ||
    !Number.isInteger(port) ||
    port < 1 ||
    port > 65535 ||
    path !== MOBILE_SNAPSHOT_PATH ||
    readOnly !== true
  ) {
    return null;
  }

  return {
    serviceUrl: `http://${host}:${port}${MOBILE_SNAPSHOT_PATH}`,
    name: typeof response.name === "string" ? response.name : "",
  };
}

function tryParseJson(value: string): unknown {
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

export async function discoverWindowsCatalog(bridge: DiscoveryBridge = nativeDiscoveryBridge): Promise<DiscoveredCatalog[]> {
  const result = await bridge.discover({ timeoutMs: DISCOVERY_TIMEOUT_MS });
  const discovered = (result.responses ?? []).map(validateDiscoveryResponse).filter((item): item is DiscoveredCatalog => item !== null);
  return [...new Map(discovered.map((item) => [item.serviceUrl, item])).values()];
}

function validateServiceUrl(serviceUrl: string): string {
  let url: URL;
  try {
    url = new URL(serviceUrl);
  } catch {
    throw new Error("发现的 Windows 地址无效");
  }

  const port = Number(url.port);
  if (
    url.protocol !== "http:" ||
    !isPrivateLanIpv4(url.hostname) ||
    !Number.isInteger(port) ||
    port < 1 ||
    port > 65535 ||
    url.pathname !== MOBILE_SNAPSHOT_PATH ||
    url.search ||
    url.hash
  ) {
    throw new Error("发现的 Windows 地址无效");
  }

  return url.toString();
}

export function restoreDiscoveredCatalog(serviceUrl: string | null): DiscoveredCatalog | null {
  if (!serviceUrl) {
    return null;
  }

  try {
    return { serviceUrl: validateServiceUrl(serviceUrl), name: "电子元件入库台" };
  } catch {
    return null;
  }
}

export async function fetchSnapshotFromPc(
  catalog: DiscoveredCatalog,
  bridge: HttpBridge = nativeHttpBridge,
): Promise<ExportCatalogSnapshot> {
  const serviceUrl = validateServiceUrl(catalog.serviceUrl);
  let response: Awaited<ReturnType<HttpBridge["get"]>>;
  try {
    response = await bridge.get({
      url: serviceUrl,
      headers: { Accept: "application/json" },
      connectTimeout: 5000,
      readTimeout: 10000,
    });
  } catch (error) {
    const endpoint = new URL(serviceUrl).host;
    const detail = error instanceof Error && error.message ? `：${error.message}` : "";
    throw new Error(`无法连接 Windows ${endpoint}${detail}`);
  }

  if (response.status < 200 || response.status >= 300) {
    throw new Error(`目录同步失败: ${response.status}`);
  }

  const payload = typeof response.data === "string" ? tryParseJson(response.data) : response.data;
  const normalized = normalizeSnapshot(payload);
  if (normalized.status === "invalid") {
    throw new Error("目录快照结构无效");
  }

  return normalized.snapshot;
}
