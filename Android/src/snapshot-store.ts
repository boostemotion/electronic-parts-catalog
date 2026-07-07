import { buildCatalogSnapshot, searchComponents, parseCsvRows } from "@shared/catalog";
import type { ComponentRecord, ExportCatalogSnapshot, SearchQuery, SearchResult } from "@shared/types";

const STORAGE_KEY = "catalog.snapshot";

export function loadSnapshot(): ExportCatalogSnapshot {
  const saved = window.localStorage.getItem(STORAGE_KEY);
  if (!saved) {
    return buildCatalogSnapshot([], new Date().toISOString());
  }

  try {
    return JSON.parse(saved) as ExportCatalogSnapshot;
  } catch {
    return buildCatalogSnapshot([], new Date().toISOString());
  }
}

export function saveSnapshot(snapshot: ExportCatalogSnapshot): void {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(snapshot));
}

export function searchSnapshot(snapshot: ExportCatalogSnapshot, query: SearchQuery): SearchResult[] {
  return searchComponents(snapshot.components, query);
}

export function generateLocalId(snapshot: ExportCatalogSnapshot, name: string, partNumber: string): string {
  const seed = partNumber || name || "component";
  const candidate = seed.trim().toLowerCase().replace(/[^a-zA-Z0-9-]/g, "-").replace(/-+/g, "-");
  let id = candidate || "component";
  while (snapshot.components.some((c) => c.id === id)) {
    id = `${candidate}-${Math.random().toString(36).substring(2, 8)}`;
  }
  return id;
}

export function addLocalComponent(snapshot: ExportCatalogSnapshot, component: ComponentRecord): ExportCatalogSnapshot {
  const updatedComponents = [...snapshot.components, component];
  const newSnapshot = buildCatalogSnapshot(updatedComponents, new Date().toISOString());
  saveSnapshot(newSnapshot);
  return newSnapshot;
}

export function updateLocalComponent(snapshot: ExportCatalogSnapshot, component: ComponentRecord): ExportCatalogSnapshot {
  const updatedComponents = snapshot.components.map((c) => (c.id === component.id ? component : c));
  const newSnapshot = buildCatalogSnapshot(updatedComponents, new Date().toISOString());
  saveSnapshot(newSnapshot);
  return newSnapshot;
}

export function deleteLocalComponent(snapshot: ExportCatalogSnapshot, id: string): ExportCatalogSnapshot {
  const updatedComponents = snapshot.components.filter((c) => c.id !== id);
  const newSnapshot = buildCatalogSnapshot(updatedComponents, new Date().toISOString());
  saveSnapshot(newSnapshot);
  return newSnapshot;
}

export function importCsvToSnapshot(
  snapshot: ExportCatalogSnapshot,
  csvText: string,
  strategy: "skip" | "merge" | "overwrite"
): { snapshot: ExportCatalogSnapshot; validCount: number; errorCount: number; firstError?: string } {
  const { validRows, errors } = parseCsvRows(csvText);
  if (errors.length > 0 && validRows.length === 0) {
    return {
      snapshot,
      validCount: 0,
      errorCount: errors.length,
      firstError: `第 ${errors[0].line} 行: ${errors[0].reason}`,
    };
  }

  const updatedComponents = [...snapshot.components];

  validRows.forEach((row) => {
    // 检查 partNumber 冲突
    const existingIndex = updatedComponents.findIndex(
      (c) => c.partNumber.trim().toLowerCase() === row.partNumber.trim().toLowerCase()
    );

    if (existingIndex >= 0) {
      if (strategy === "skip") {
        return; // 跳过
      } else if (strategy === "overwrite") {
        // 覆盖
        const existing = updatedComponents[existingIndex];
        updatedComponents[existingIndex] = {
          ...row,
          id: existing.id, // 保留原 ID
          substitutes: existing.substitutes || [],
        };
      } else if (strategy === "merge") {
        // 合并
        const existing = updatedComponents[existingIndex];
        updatedComponents[existingIndex] = {
          ...existing,
          aliases: Array.from(new Set([...existing.aliases, ...row.aliases])),
          keywords: Array.from(new Set([...existing.keywords, ...row.keywords])),
          substitutes: existing.substitutes || [],
          attributes: { ...existing.attributes, ...row.attributes },
          notes: existing.notes + (row.notes ? `\n[合并备注] ${row.notes}` : ""),
        };
      }
    } else {
      // 不冲突，直接新增
      const seed = row.partNumber || row.name || "component";
      const candidate = seed.trim().toLowerCase().replace(/[^a-zA-Z0-9-]/g, "-").replace(/-+/g, "-");
      let newId = candidate || "component";
      while (updatedComponents.some((c) => c.id === newId)) {
        newId = `${candidate}-${Math.random().toString(36).substring(2, 8)}`;
      }

      updatedComponents.push({
        ...row,
        id: newId,
        substitutes: [],
      });
    }
  });

  const newSnapshot = buildCatalogSnapshot(updatedComponents, new Date().toISOString());
  saveSnapshot(newSnapshot);

  return {
    snapshot: newSnapshot,
    validCount: validRows.length,
    errorCount: errors.length,
    firstError: errors.length > 0 ? `第 ${errors[0].line} 行: ${errors[0].reason}` : undefined,
  };
}
