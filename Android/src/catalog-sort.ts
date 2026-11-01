import type { SearchResult } from "@shared/types";

export type SortOrder = "relevance" | "name" | "partNumber" | "category";

export const sortOrderLabels: Record<SortOrder, string> = {
  relevance: "匹配度",
  name: "名称",
  partNumber: "型号",
  category: "分类",
};

export const sortOrderOptions: SortOrder[] = ["relevance", "name", "partNumber", "category"];

export function sortSearchResults(results: SearchResult[], sortOrder: SortOrder): SearchResult[] {
  if (sortOrder === "relevance") {
    return results;
  }

  const compareText = (left: string, right: string) => left.localeCompare(right, "zh-CN");
  const key = (result: SearchResult) => {
    switch (sortOrder) {
      case "name":
        return result.component.name;
      case "partNumber":
        return result.component.partNumber;
      case "category":
        return result.component.category;
      default:
        return result.component.name;
    }
  };

  return [...results].sort(
    (left, right) => compareText(key(left), key(right)) || compareText(left.component.name, right.component.name),
  );
}
