import type { ComponentRecord, ExportCatalogSnapshot } from "@shared/types";

interface ComponentsResponse {
  results?: Array<{ component: ComponentRecord }>;
}

interface CategoriesResponse {
  categories?: string[];
}

export function normalizePcAddress(address: string): string {
  const trimmed = address.trim().replace(/\/+$/, "");
  if (!trimmed) {
    return "";
  }

  return /^https?:\/\//i.test(trimmed) ? trimmed : `http://${trimmed}`;
}

export async function fetchSnapshotFromPc(address: string): Promise<ExportCatalogSnapshot> {
  const baseUrl = normalizePcAddress(address);
  if (!baseUrl) {
    throw new Error("PC 地址为空");
  }

  const [componentsResponse, categoriesResponse] = await Promise.all([
    fetch(`${baseUrl}/components`, { method: "GET", mode: "cors" }),
    fetch(`${baseUrl}/categories`, { method: "GET", mode: "cors" }),
  ]);

  if (!componentsResponse.ok) {
    throw new Error(`元件接口拉取失败: ${componentsResponse.status}`);
  }

  if (!categoriesResponse.ok) {
    throw new Error(`分类接口拉取失败: ${categoriesResponse.status}`);
  }

  const componentsData = (await componentsResponse.json()) as ComponentsResponse;
  const categoriesData = (await categoriesResponse.json()) as CategoriesResponse;
  const components = (componentsData.results ?? []).map((result) => result.component);
  const categories = (categoriesData.categories ?? []).filter((category) => category !== "全部");

  return {
    meta: {
      exportedAt: new Date().toISOString(),
      componentCount: components.length,
      categories,
    },
    components,
  };
}

export async function upsertComponentToPc(address: string, component: ComponentRecord): Promise<void> {
  const baseUrl = normalizePcAddress(address);
  if (!baseUrl) {
    return;
  }

  const response = await fetch(`${baseUrl}/components`, {
    method: "POST",
    mode: "cors",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(component),
  });

  if (!response.ok) {
    throw new Error(`PC 写入失败: ${response.status}`);
  }
}

export async function deleteComponentFromPc(address: string, id: string): Promise<void> {
  const baseUrl = normalizePcAddress(address);
  if (!baseUrl) {
    return;
  }

  const response = await fetch(`${baseUrl}/components/${encodeURIComponent(id)}`, {
    method: "DELETE",
    mode: "cors",
  });

  if (!response.ok) {
    throw new Error(`PC 删除失败: ${response.status}`);
  }
}
