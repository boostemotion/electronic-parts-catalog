import { buildCatalogSnapshot, searchComponents } from "@shared/catalog";
import { TAXONOMY_VERSION } from "./version.generated";
import type {
  ComponentMajorCategory,
  ComponentRecord,
  ExportCatalogSnapshot,
  SearchQuery,
  SearchResult,
} from "@shared/types";

const STORAGE_KEY = "catalog.snapshot";

const LEGACY_CATEGORY_ASSIGNMENTS: Record<string, { majorCategoryId: string; subCategoryId: string }> = {
  "贴片电容": { majorCategoryId: "passives", subCategoryId: "mlcc" },
  "电容": { majorCategoryId: "passives", subCategoryId: "mlcc" },
  "贴片电阻": { majorCategoryId: "passives", subCategoryId: "chip-resistor" },
  "贴片电感": { majorCategoryId: "passives", subCategoryId: "chip-inductor" },
  "电感": { majorCategoryId: "passives", subCategoryId: "chip-inductor" },
  "微控制器(MCU)": { majorCategoryId: "mcu-logic", subCategoryId: "mcu-mpu-soc" },
  MCU: { majorCategoryId: "mcu-logic", subCategoryId: "mcu-mpu-soc" },
  "集成电路": { majorCategoryId: "mcu-logic", subCategoryId: "mcu-mpu-soc" },
  "二极管": { majorCategoryId: "diodes-transistors", subCategoryId: "rectifier-diode" },
  "三极管": { majorCategoryId: "diodes-transistors", subCategoryId: "bjt" },
  "电源": { majorCategoryId: "protection-power", subCategoryId: "power-other" },
  "连接器": { majorCategoryId: "connectors-switches", subCategoryId: "connector-other" },
  "晶振": { majorCategoryId: "filters-oscillators", subCategoryId: "passive-crystal" },
  "其他": { majorCategoryId: "other", subCategoryId: "uncategorized" },
  "未分类": { majorCategoryId: "other", subCategoryId: "uncategorized" },
  "": { majorCategoryId: "other", subCategoryId: "uncategorized" },
};

export type SnapshotNormalizationStatus = "current" | "upgraded" | "legacy-read-only" | "invalid";

export interface SnapshotNormalizationResult {
  snapshot: ExportCatalogSnapshot;
  status: SnapshotNormalizationStatus;
}

function createEmptySnapshot(): ExportCatalogSnapshot {
  return {
    meta: {
      exportedAt: "",
      componentCount: 0,
      categories: [],
      taxonomyVersion: TAXONOMY_VERSION,
      majorCategories: [],
    },
    components: [],
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === "string");
}

function isStringMap(value: unknown): value is Record<string, string> {
  return isRecord(value) && Object.values(value).every((item) => typeof item === "string");
}

function isComponentRecord(value: unknown): value is ComponentRecord {
  if (!isRecord(value)) {
    return false;
  }

  return (
    typeof value.id === "string" &&
    typeof value.name === "string" &&
    typeof value.partNumber === "string" &&
    typeof value.category === "string" &&
    typeof value.package === "string" &&
    isStringArray(value.aliases) &&
    isStringArray(value.keywords) &&
    isStringMap(value.attributes) &&
    typeof value.owned === "boolean" &&
    typeof value.location === "string" &&
    isStringArray(value.substitutes) &&
    typeof value.notes === "string" &&
    (value.sourceType === "manual" || value.sourceType === "csv-import") &&
    (value.majorCategoryId === undefined || typeof value.majorCategoryId === "string") &&
    (value.subCategoryId === undefined || typeof value.subCategoryId === "string") &&
    (value.quantity === undefined || (typeof value.quantity === "number" && Number.isInteger(value.quantity) && value.quantity >= 0)) &&
    (value.designators === undefined || isStringArray(value.designators))
  );
}

function isMajorCategories(value: unknown): value is ComponentMajorCategory[] {
  return (
    Array.isArray(value) &&
    value.every(
      (majorCategory) =>
        isRecord(majorCategory) &&
        typeof majorCategory.id === "string" &&
        typeof majorCategory.label === "string" &&
        typeof majorCategory.order === "number" &&
        Array.isArray(majorCategory.subcategories) &&
        majorCategory.subcategories.every(
          (subcategory) =>
            isRecord(subcategory) &&
            typeof subcategory.id === "string" &&
            typeof subcategory.majorCategoryId === "string" &&
            typeof subcategory.label === "string" &&
            typeof subcategory.order === "number",
        ),
    )
  );
}

function getCategories(components: ComponentRecord[], metadata: Record<string, unknown>): string[] {
  if (isStringArray(metadata.categories)) {
    return metadata.categories;
  }

  return [...new Set(components.map((component) => component.category))].sort((left, right) =>
    left.localeCompare(right, "zh-CN"),
  );
}

function hasTaxonomyIds(component: ComponentRecord): boolean {
  return Boolean(component.majorCategoryId?.trim() && component.subCategoryId?.trim());
}

function hasValidTaxonomyAssignment(component: ComponentRecord, majorCategories: ComponentMajorCategory[]): boolean {
  const majorCategory = majorCategories.find((category) => category.id === component.majorCategoryId);
  return Boolean(majorCategory?.subcategories.some((category) => category.id === component.subCategoryId));
}

export function normalizeSnapshot(value: unknown): SnapshotNormalizationResult {
  if (!isRecord(value) || !isRecord(value.meta) || !Array.isArray(value.components) || !value.components.every(isComponentRecord)) {
    return { snapshot: createEmptySnapshot(), status: "invalid" };
  }

  const metadata = value.meta;
  const components = value.components;
  const majorCategories = isMajorCategories(metadata.majorCategories) ? metadata.majorCategories : [];
  const isCurrent =
    metadata.taxonomyVersion === TAXONOMY_VERSION &&
    isMajorCategories(metadata.majorCategories) &&
    components.every((component) => hasValidTaxonomyAssignment(component, majorCategories));

  if (isCurrent) {
    return { snapshot: value as unknown as ExportCatalogSnapshot, status: "current" };
  }

  let hasAmbiguousLegacyRecord = false;
  const upgradedComponents = components.map((component) => {
    if (hasTaxonomyIds(component) && (!majorCategories.length || hasValidTaxonomyAssignment(component, majorCategories))) {
      return component;
    }

    const assignment = LEGACY_CATEGORY_ASSIGNMENTS[component.category.trim()];
    if (assignment) {
      const upgradedComponent = { ...component, ...assignment };
      if (!majorCategories.length || hasValidTaxonomyAssignment(upgradedComponent, majorCategories)) {
        return upgradedComponent;
      }
    }

    hasAmbiguousLegacyRecord = true;
    const { majorCategoryId: _majorCategoryId, subCategoryId: _subCategoryId, ...legacyComponent } = component;
    return legacyComponent;
  });

  return {
    snapshot: {
      meta: {
        exportedAt: typeof metadata.exportedAt === "string" ? metadata.exportedAt : "",
        componentCount: upgradedComponents.length,
        categories: getCategories(upgradedComponents, metadata),
        taxonomyVersion: typeof metadata.taxonomyVersion === "string" ? metadata.taxonomyVersion : TAXONOMY_VERSION,
        majorCategories: isMajorCategories(metadata.majorCategories) ? metadata.majorCategories : [],
      },
      components: upgradedComponents,
    },
    status: hasAmbiguousLegacyRecord ? "legacy-read-only" : "upgraded",
  };
}

export function loadSnapshotResult(): SnapshotNormalizationResult {
  const saved = window.localStorage.getItem(STORAGE_KEY);
  if (!saved) {
    return { snapshot: buildCatalogSnapshot([], new Date().toISOString()), status: "current" };
  }

  try {
    return normalizeSnapshot(JSON.parse(saved));
  } catch {
    return { snapshot: createEmptySnapshot(), status: "invalid" };
  }
}

export function loadSnapshot(): ExportCatalogSnapshot {
  return loadSnapshotResult().snapshot;
}

export function saveSnapshot(snapshot: ExportCatalogSnapshot): void {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(snapshot));
}

export function searchSnapshot(snapshot: ExportCatalogSnapshot, query: SearchQuery): SearchResult[] {
  return searchComponents(snapshot.components, query);
}
