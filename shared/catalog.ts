import { TAXONOMY_VERSION } from "./version.generated";
import type {
  ComponentAttributeMap,
  ComponentMajorCategory,
  ComponentRecord,
  ExportCatalogSnapshot,
  ImportParseResult,
  ImportRow,
  SearchQuery,
  SearchResult,
} from "./types.js";

const FIELD_DELIMITER = /[;\n]/;
const TOKEN_SPLIT = /\s+/;

function normalize(value: string): string {
  return value.trim().toLowerCase();
}

function tokenize(value: string): string[] {
  return normalize(value)
    .split(TOKEN_SPLIT)
    .map((part) => part.trim())
    .filter(Boolean);
}

function parseBoolean(value: string): boolean {
  return ["true", "1", "yes", "y"].includes(normalize(value));
}

function splitList(value: string): string[] {
  return value
    .split(/[|,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function csvValue(line: string): string[] {
  const fields: string[] = [];
  let current = "";
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const ch = line[i];

    if (inQuotes) {
      if (ch === '"') {
        // RFC 4180: "" 转义为单个 "
        if (i + 1 < line.length && line[i + 1] === '"') {
          current += '"';
          i++;
        } else {
          inQuotes = false;
        }
      } else {
        current += ch;
      }
    } else {
      if (ch === '"') {
        inQuotes = true;
      } else if (ch === ",") {
        fields.push(current.trim());
        current = "";
      } else {
        current += ch;
      }
    }
  }

  fields.push(current.trim());
  return fields;
}

function createSearchHaystack(component: ComponentRecord): string[] {
  return [
    component.name,
    component.partNumber,
    component.package,
    component.category,
    ...component.aliases,
    ...component.keywords,
    ...Object.entries(component.attributes).flatMap(([key, value]) => [key, value]),
  ].map(normalize);
}

export function parseAttributes(input: string): ComponentAttributeMap {
  return input
    .split(FIELD_DELIMITER)
    .map((pair) => pair.trim())
    .filter(Boolean)
    .reduce<ComponentAttributeMap>((accumulator, pair) => {
      const separatorIndex = pair.indexOf("=");
      if (separatorIndex === -1) {
        return accumulator;
      }

      const key = pair.slice(0, separatorIndex).trim();
      const value = pair.slice(separatorIndex + 1).trim();
      if (key && value) {
        accumulator[key] = value;
      }
      return accumulator;
    }, {});
}

export function parseCsvRows(input: string): ImportParseResult {
  const lines = input
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  const [headerLine, ...rows] = lines;
  const headers = csvValue(headerLine ?? "");
  const validRows: ImportRow[] = [];
  const errors: ImportParseResult["errors"] = [];

  rows.forEach((row, index) => {
    const values = csvValue(row);
    const record = headers.reduce<Record<string, string>>((accumulator, header, headerIndex) => {
      accumulator[header] = values[headerIndex] ?? "";
      return accumulator;
    }, {});

    if (!record.name) {
      errors.push({
        line: index + 2,
        reason: "name is required",
      });
      return;
    }

    if (!record.partNumber) {
      errors.push({
        line: index + 2,
        reason: "partNumber is required",
      });
      return;
    }

    validRows.push({
      name: record.name,
      partNumber: record.partNumber,
      category: record.category ?? "未分类",
      package: record.package ?? "",
      owned: parseBoolean(record.owned ?? "false"),
      attributes: parseAttributes(record.attributes ?? ""),
      aliases: splitList(record.aliases ?? ""),
      keywords: splitList(record.keywords ?? ""),
      location: record.location ?? "",
      notes: record.notes ?? "",
      sourceType: "csv-import",
    });
  });

  return { validRows, errors };
}

function calculateScore(component: ComponentRecord, query: SearchQuery): SearchResult | null {
  const text = normalize(query.text);
  const tokens = tokenize(query.text);
  const matchedFields = new Set<string>();
  let score = 0;

  if (query.category !== "全部" && component.category !== query.category) {
    return null;
  }

  if (query.majorCategoryId && component.majorCategoryId !== query.majorCategoryId) {
    return null;
  }

  if (query.subCategoryId && component.subCategoryId !== query.subCategoryId) {
    return null;
  }

  if (query.package && component.package !== query.package) {
    return null;
  }

  if (typeof query.owned === "boolean" && component.owned !== query.owned) {
    return null;
  }

  if (query.attributeText && !matchesAttributeFilter(component, query.attributeText)) {
    return null;
  }

  if (!text) {
    return {
      component,
      score: 1,
      matchedFields: ["category"],
    };
  }

  if (normalize(component.partNumber) === text) {
    score += 1000;
    matchedFields.add("partNumber");
  }

  component.aliases.forEach((alias: string) => {
    if (normalize(alias).includes(text)) {
      score += 500;
      matchedFields.add("aliases");
    }
  });

  createSearchHaystack(component).forEach((field) => {
    if (field.includes(text)) {
      score += 80;
      matchedFields.add("keywords");
    }
  });

  tokens.forEach((token) => {
    if (normalize(component.package).includes(token)) {
      score += 50;
      matchedFields.add("package");
    }

    if (Object.entries(component.attributes).some(([key, value]) => normalize(`${key} ${value}`).includes(token))) {
      score += 40;
      matchedFields.add("attributes");
    }

    if (normalize(component.name).includes(token) || normalize(component.partNumber).includes(token)) {
      score += 30;
      matchedFields.add("name");
    }
  });

  if (score === 0) {
    return null;
  }

  return {
    component,
    score,
    matchedFields: [...matchedFields],
  };
}

export function searchComponents(components: ComponentRecord[], query: SearchQuery): SearchResult[] {
  return components
    .map((component) => calculateScore(component, query))
    .filter((result): result is SearchResult => result !== null)
    .sort((left, right) => right.score - left.score || left.component.name.localeCompare(right.component.name, "zh-CN"));
}

export function buildCatalogSnapshot(
  components: ComponentRecord[],
  exportedAt: string,
  taxonomy: { taxonomyVersion: string; majorCategories: ComponentMajorCategory[] } = {
    taxonomyVersion: TAXONOMY_VERSION,
    majorCategories: [],
  },
): ExportCatalogSnapshot {
  const categories = [...new Set(components.map((component) => component.category))].sort((left, right) =>
    left.localeCompare(right, "zh-CN"),
  );

  return {
    meta: {
      exportedAt,
      componentCount: components.length,
      categories,
      taxonomyVersion: taxonomy.taxonomyVersion,
      majorCategories: taxonomy.majorCategories,
    },
    components,
  };
}

function matchesAttributeFilter(component: ComponentRecord, attributeText: string): boolean {
  const normalizedFilter = normalize(attributeText);
  if (!normalizedFilter) {
    return true;
  }

  const separatorIndex = normalizedFilter.indexOf("=");
  if (separatorIndex >= 0) {
    const key = normalizedFilter.slice(0, separatorIndex).trim();
    const value = normalizedFilter.slice(separatorIndex + 1).trim();
    return Object.entries(component.attributes).some(
      ([k, v]) => normalize(k) === key && normalize(v).includes(value)
    );
  }

  return Object.entries(component.attributes).some(
    ([k, v]) =>
      normalize(k).includes(normalizedFilter) ||
      normalize(v).includes(normalizedFilter) ||
      normalize(`${k}=${v}`).includes(normalizedFilter)
  );
}
