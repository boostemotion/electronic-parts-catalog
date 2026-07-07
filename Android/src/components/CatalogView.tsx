import React, { useState } from "react";
import type { ComponentRecord, ExportCatalogSnapshot, SearchResult } from "@shared/types";
import { AppIcon } from "./AppIcon";
import { useBottomSheetDragDismiss } from "./useBottomSheetDragDismiss";

const matchedFieldLabelMap: Record<string, string> = {
  name: "名称",
  partNumber: "型号",
  aliases: "别名",
  keywords: "关键词",
  package: "封装",
  attributes: "参数",
  category: "分类",
};

const sourceTypeLabelMap: Record<string, string> = {
  manual: "手动录入",
  "csv-import": "CSV 导入",
};

interface CatalogViewProps {
  snapshot: ExportCatalogSnapshot;
  categories: string[];
  results: SearchResult[];
  selectedCategory: string;
  onSelectCategory: (category: string) => void;
  searchText: string;
  onSearchChange: (text: string) => void;
  onSelectComponent: (component: ComponentRecord) => void;
  packages: string[];
  selectedPackage: string;
  onSelectPackage: (pkg: string) => void;
  selectedOwned: string;
  onSelectOwned: (owned: string) => void;
  attributeFilter: string;
  onAttributeFilterChange: (attr: string) => void;
  onNewComponent: () => void;
  onOpenSync: () => void;
}

export const CatalogView: React.FC<CatalogViewProps> = ({
  snapshot,
  categories,
  results,
  selectedCategory,
  onSelectCategory,
  searchText,
  onSearchChange,
  onSelectComponent,
  packages,
  selectedPackage,
  onSelectPackage,
  selectedOwned,
  onSelectOwned,
  attributeFilter,
  onAttributeFilterChange,
  onNewComponent,
  onOpenSync,
}) => {
  const [filterSheetOpen, setFilterSheetOpen] = useState(false);
  const filterSheetDrag = useBottomSheetDragDismiss(() => setFilterSheetOpen(false));

  const ownedCount = snapshot.components.filter((component) => component.owned).length;
  const activeFilterCount = [
    selectedCategory !== "全部",
    selectedPackage !== "全部封装",
    selectedOwned !== "全部",
    attributeFilter.trim().length > 0,
  ].filter(Boolean).length;

  const getMatchedFieldLabel = (field: string) => matchedFieldLabelMap[field] || field;
  const getSourceTypeLabel = (sourceType: string) => sourceTypeLabelMap[sourceType] || sourceType;

  return (
    <div className="md3-catalog-container">
      <header className="md3-top-app-bar md3-animate-enter">
        <div className="md3-top-app-bar__row">
          <div>
            <h1>随手查料</h1>
          </div>
          <button
            type="button"
            className="md3-icon-pill"
            aria-label="新建元件"
            onClick={onNewComponent}
          >
            <AppIcon name="add" className="md3-inline-icon" />
          </button>
        </div>
      </header>

      <section className="md3-summary-grid">
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "40ms" }}>
          <span className="md3-summary-card__label">目录总数</span>
          <strong className="md3-summary-card__value">{snapshot.meta.componentCount}</strong>
        </article>
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "80ms" }}>
          <span className="md3-summary-card__label">分类数</span>
          <strong className="md3-summary-card__value">{snapshot.meta.categories.length}</strong>
        </article>
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "120ms" }}>
          <span className="md3-summary-card__label">已持有</span>
          <strong className="md3-summary-card__value">{ownedCount}</strong>
        </article>
      </section>

      <div className="md3-search-bar-container md3-animate-enter" style={{ animationDelay: "160ms" }}>
        <span className="md3-search-icon">
          <AppIcon name="search" className="md3-inline-icon md3-inline-icon-search" />
        </span>
        <input
          type="search"
          className="md3-search-bar md3-search-bar-with-actions"
          value={searchText}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="搜索型号、封装、参数..."
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          enterKeyHint="search"
        />
        <div className="md3-search-actions">
          <button
            type="button"
            className="md3-chip md3-chip-action"
            aria-label="筛选与排序"
            onClick={() => setFilterSheetOpen(true)}
          >
            筛选
          </button>
          {activeFilterCount > 0 && <span className="md3-inline-count">{activeFilterCount}</span>}
        </div>
      </div>

      <div className="md3-section-head md3-animate-enter" style={{ animationDelay: "200ms" }}>
        <h2>结果</h2>
        <span>{results.length} 条结果</span>
      </div>

      <div className="md3-chips-scroll md3-animate-enter" style={{ animationDelay: "240ms" }}>
        {["全部", ...categories].map((category) => (
          <button
            key={category}
            type="button"
            className={`md3-chip ${category === selectedCategory ? "active" : ""}`}
            onClick={() => onSelectCategory(category)}
          >
            {category}
          </button>
        ))}
      </div>

      <div className="md3-list">
        {snapshot.meta.componentCount === 0 ? (
          <div className="md3-empty-state md3-animate-enter" style={{ animationDelay: "280ms" }}>
            <div className="md3-empty-state__icon">⌕</div>
            <p className="md3-empty-state__title">还没有元件目录</p>
            <p className="md3-empty-state__body">先去数据同步导入快照，或者从 Windows 端拉取目录。</p>
            <div className="md3-empty-state__actions">
              <button type="button" className="md3-btn-filled md3-btn-inline" onClick={onOpenSync}>
                前往同步
              </button>
            </div>
          </div>
        ) : results.length === 0 ? (
          <div className="md3-empty-state md3-animate-enter" style={{ animationDelay: "280ms" }}>
            <div className="md3-empty-state__icon">⌕</div>
            <p className="md3-empty-state__title">没有匹配结果</p>
            <p className="md3-empty-state__body">换个关键词或清空筛选。</p>
          </div>
        ) : (
          results.map((result, index) => (
            <button
              key={result.component.id}
              type="button"
              className="md3-catalog-card md3-animate-enter"
              style={{ animationDelay: `${280 + index * 36}ms` }}
              onClick={() => onSelectComponent(result.component)}
            >
              <div className="md3-catalog-card__top">
                <div>
                  <h3>{result.component.name}</h3>
                  <div className="part-number">{result.component.partNumber}</div>
                </div>
                <span className={`md3-presence-pill ${result.component.owned ? "owned" : "missing"}`}>
                  {result.component.owned ? "已持有" : "未持有"}
                </span>
              </div>

              <div className="md3-card-tags">
                <span className="md3-badge md3-badge-accent">{result.component.category}</span>
                <span className="md3-badge">{result.component.package}</span>
                {result.matchedFields.length > 0 && (
                  <span className="md3-badge md3-badge-subtle">命中 {getMatchedFieldLabel(result.matchedFields[0])}</span>
                )}
              </div>

              <div className="md3-catalog-card__meta">
                <span>位置 · {result.component.location || "未记录"}</span>
                <span>来源 · {getSourceTypeLabel(result.component.sourceType)}</span>
              </div>

            </button>
          ))
        )}
      </div>

      {filterSheetOpen && (
        <>
          <div
            className="md3-bottom-sheet-scrim open"
            onClick={() => setFilterSheetOpen(false)}
            style={filterSheetDrag.scrimStyle}
          />
          <div
            className={`md3-bottom-sheet md3-filter-sheet open ${filterSheetDrag.isDragging ? "dragging" : ""}`}
            style={filterSheetDrag.sheetStyle}
          >
            <div className="md3-bottom-sheet-drag-handle" {...filterSheetDrag.dragHandleProps} />
            <div className="md3-bottom-sheet-content">
              <div className="md3-sheet-header">
                <div>
                  <h2 className="md3-sheet-title">筛选与排序</h2>
                </div>
                <button
                  type="button"
                  className="md3-text-action"
                  onClick={() => {
                    onSelectCategory("全部");
                    onSelectPackage("全部封装");
                    onSelectOwned("全部");
                    onAttributeFilterChange("");
                  }}
                >
                  重置
                </button>
              </div>

              <div className="md3-filter-grid">
                <div className="md3-form-group">
                  <label>封装</label>
                  <select
                    value={selectedPackage}
                    onChange={(e) => onSelectPackage(e.target.value)}
                    className="md3-select-field"
                  >
                    <option value="全部封装">全部封装</option>
                    {packages.map((pkg) => (
                      <option key={pkg} value={pkg}>
                        {pkg}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="md3-form-group">
                  <label>持有状态</label>
                  <select
                    value={selectedOwned}
                    onChange={(e) => onSelectOwned(e.target.value)}
                    className="md3-select-field"
                  >
                    <option value="全部">全部</option>
                    <option value="仅持有">仅持有</option>
                    <option value="仅未持有">仅未持有</option>
                  </select>
                </div>
              </div>

              <div className="md3-form-group">
                <label>属性过滤</label>
                <input
                  type="text"
                  className="md3-input-field md3-input-field-compact"
                  value={attributeFilter}
                  onChange={(e) => onAttributeFilterChange(e.target.value)}
                  placeholder="例如 voltage=3.3V"
                  autoCapitalize="off"
                  autoCorrect="off"
                  spellCheck={false}
                />
              </div>

              <div className="md3-sheet-actions">
                <button
                  type="button"
                  className="md3-btn-filled"
                  onClick={() => setFilterSheetOpen(false)}
                >
                  应用筛选
                </button>
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  );
};
