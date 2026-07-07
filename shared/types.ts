export type ComponentSourceType = "manual" | "csv-import";

export type ComponentAttributeMap = Record<string, string>;

export interface ComponentRecord {
  id: string;
  name: string;
  partNumber: string;
  category: string;
  package: string;
  aliases: string[];
  keywords: string[];
  attributes: ComponentAttributeMap;
  owned: boolean;
  location: string;
  substitutes: string[];
  notes: string;
  sourceType: ComponentSourceType;
}

export interface SearchQuery {
  text: string;
  category: string;
  package?: string;
  owned?: boolean;
  attributeText?: string;
}

export interface SearchResult {
  component: ComponentRecord;
  score: number;
  matchedFields: string[];
}

export interface ImportRow {
  name: string;
  partNumber: string;
  category: string;
  package: string;
  owned: boolean;
  attributes: ComponentAttributeMap;
  aliases: string[];
  keywords: string[];
  location: string;
  notes: string;
  sourceType: ComponentSourceType;
}

export interface ImportError {
  line: number;
  reason: string;
}

export interface ImportParseResult {
  validRows: ImportRow[];
  errors: ImportError[];
}

export interface ExportCatalogSnapshot {
  meta: {
    exportedAt: string;
    componentCount: number;
    categories: string[];
  };
  components: ComponentRecord[];
}
