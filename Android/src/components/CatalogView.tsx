import React, { useMemo, useState, useEffect } from "react";
import type { ComponentMajorCategory, ComponentRecord, ExportCatalogSnapshot, SearchResult } from "@shared/types";
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
  majorCategoryId: "大类",
  subCategoryId: "子类",
};

const sourceTypeLabelMap: Record<string, string> = {
  manual: "手动录入",
  "csv-import": "CSV 导入",
};

interface CatalogViewProps {
  snapshot: ExportCatalogSnapshot;
  majorCategories: ComponentMajorCategory[];
  results: SearchResult[];
  selectedMajorCategoryId: string;
  onSelectMajorCategoryId: (majorCategoryId: string) => void;
  selectedSubCategoryId: string;
  onSelectSubCategoryId: (subCategoryId: string) => void;
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
  onOpenSync: () => void;
}

function formatCategoryLabel(component: ComponentRecord, majorCategories: ComponentMajorCategory[]): string {
  const majorCategory = majorCategories.find((category) => category.id === component.majorCategoryId);
  const subCategory = majorCategory?.subcategories.find((category) => category.id === component.subCategoryId);
  return majorCategory && subCategory ? `${majorCategory.label} · ${subCategory.label}` : component.category;
}

export const CatalogView: React.FC<CatalogViewProps> = ({
  snapshot,
  majorCategories,
  results,
  selectedMajorCategoryId,
  onSelectMajorCategoryId,
  selectedSubCategoryId,
  onSelectSubCategoryId,
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
  onOpenSync,
}) => {
  const [filterSheetOpen, setFilterSheetOpen] = useState(false);
  const [activeFilterView, setActiveFilterView] = useState<'main' | 'majorCategory' | 'subCategory' | 'package'>('main');
  const filterSheetDrag = useBottomSheetDragDismiss(() => setFilterSheetOpen(false));

  const selectedMajorCategory = majorCategories.find((category) => category.id === selectedMajorCategoryId);
  const subcategories = selectedMajorCategory?.subcategories ?? [];
  const ownedCount = snapshot.components.filter((component) => component.owned).length;
  const activeFilterCount = [
    Boolean(selectedMajorCategoryId),
    Boolean(selectedSubCategoryId),
    selectedPackage !== "全部封装",
    selectedOwned !== "全部",
    attributeFilter.trim().length > 0,
  ].filter(Boolean).length;
  const categoryLabels = useMemo(
    () => new Map(results.map(({ component }) => [component.id, formatCategoryLabel(component, majorCategories)])),
    [majorCategories, results],
  );

  useEffect(() => {
    if (!filterSheetOpen) {
      setTimeout(() => setActiveFilterView('main'), 300);
    }
  }, [filterSheetOpen]);

  const resetFilters = () => {
    onSelectMajorCategoryId("");
    onSelectSubCategoryId("");
    onSelectPackage("全部封装");
    onSelectOwned("全部");
    onAttributeFilterChange("");
  };

  const renderNestedList = (
    title: string,
    items: { value: string; label: string }[],
    selectedValue: string,
    onSelect: (val: string) => void
  ) => (
    <div className="md3-nested-view">
      <div className="md3-sheet-header md3-sheet-header-nested">
        <button type="button" className="md3-icon-button" onClick={() => setActiveFilterView('main')}>
          ‹
        </button>
        <h2 className="md3-sheet-title md3-sheet-title-nested">{title}</h2>
        <div style={{ width: 48 }}></div>
      </div>
      <div className="md3-nested-list">
        {items.map((item) => (
          <button
            key={item.value}
            type="button"
            className={`md3-nested-list-item ${selectedValue === item.value ? 'selected' : ''}`}
            onClick={() => {
              onSelect(item.value);
              setActiveFilterView('main');
            }}
          >
            {item.label}
            {selectedValue === item.value && <span className="md3-nested-list-check">✓</span>}
          </button>
        ))}
      </div>
    </div>
  );

  return (
    <div className="md3-catalog-container">
      <header className="md3-top-app-bar md3-animate-enter">
        <div className="md3-top-app-bar__row">
          <div>
            <h1>随手查料</h1>
          </div>
        </div>
      </header>


      <section className="md3-summary-grid">
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "40ms" }}>
          <span className="md3-summary-card__label">目录总数</span>
          <strong className="md3-summary-card__value">{snapshot.meta.componentCount}</strong>
        </article>
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "80ms" }}>
          <span className="md3-summary-card__label">大类数</span>
          <strong className="md3-summary-card__value">{majorCategories.length}</strong>
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
          onChange={(event) => onSearchChange(event.target.value)}
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

      <div className="md3-list">
        {snapshot.meta.componentCount === 0 ? (
          <div className="md3-empty-state md3-animate-enter" style={{ animationDelay: "280ms" }}>
            <div className="md3-empty-state__icon">⌕</div>
            <p className="md3-empty-state__title">还没有元件目录</p>
            <p className="md3-empty-state__body">请确认 Windows 已开启手机自动同步，然后重新查找目录。</p>
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
                <span className="md3-badge md3-badge-accent">{categoryLabels.get(result.component.id)}</span>
                <span className="md3-badge">{result.component.package}</span>
                {result.matchedFields.length > 0 && (
                  <span className="md3-badge md3-badge-subtle">命中 {matchedFieldLabelMap[result.matchedFields[0]] || result.matchedFields[0]}</span>
                )}
              </div>

              <div className="md3-catalog-card__meta">
                <span>位置 · {result.component.location || "未记录"}</span>
                <span>来源 · {sourceTypeLabelMap[result.component.sourceType] || result.component.sourceType}</span>
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
              {activeFilterView === 'main' && (
                <>
                  <div className="md3-sheet-header">
                    <h2 className="md3-sheet-title">筛选与排序</h2>
                    <button type="button" className="md3-text-action" onClick={resetFilters}>
                      重置
                    </button>
                  </div>

                  <div className="md3-filter-column">
                    <div className="md3-form-group">
                      <label>持有状态</label>
                      <div className="md3-segmented-buttons">
                        {['全部', '仅持有', '仅未持有'].map((opt) => (
                          <button
                            key={opt}
                            type="button"
                            className={`md3-segmented-button ${selectedOwned === opt ? 'active' : ''}`}
                            onClick={() => onSelectOwned(opt)}
                          >
                            {opt}
                          </button>
                        ))}
                      </div>
                    </div>

                    <div className="md3-list-row-tappable" onClick={() => setActiveFilterView('majorCategory')}>
                      <div className="md3-list-row-text">
                        <span className="md3-list-row-label">大类</span>
                        <span className="md3-list-row-value">
                          {selectedMajorCategoryId ? selectedMajorCategory?.label : '全部大类'}
                        </span>
                      </div>
                      <span className="md3-list-row-icon">›</span>
                    </div>

                    <div
                      className="md3-list-row-tappable"
                      onClick={() => selectedMajorCategoryId && setActiveFilterView('subCategory')}
                      style={{ opacity: selectedMajorCategoryId ? 1 : 0.5, cursor: selectedMajorCategoryId ? 'pointer' : 'default' }}
                    >
                      <div className="md3-list-row-text">
                        <span className="md3-list-row-label">子类</span>
                        <span className="md3-list-row-value">
                          {selectedSubCategoryId ? subcategories.find(c => c.id === selectedSubCategoryId)?.label : '全部子类'}
                        </span>
                      </div>
                      <span className="md3-list-row-icon">›</span>
                    </div>

                    <div className="md3-list-row-tappable" onClick={() => setActiveFilterView('package')}>
                      <div className="md3-list-row-text">
                        <span className="md3-list-row-label">封装</span>
                        <span className="md3-list-row-value">
                          {selectedPackage}
                        </span>
                      </div>
                      <span className="md3-list-row-icon">›</span>
                    </div>

                    <div className="md3-form-group" style={{ marginTop: 4 }}>
                      <label htmlFor="filter-attributes">属性过滤</label>
                      <input
                        id="filter-attributes"
                        type="text"
                        className="md3-input-field md3-input-field-compact"
                        value={attributeFilter}
                        onChange={(event) => onAttributeFilterChange(event.target.value)}
                        placeholder="例如 voltage=3.3V"
                        autoCapitalize="off"
                        autoCorrect="off"
                        spellCheck={false}
                      />
                    </div>
                  </div>

                  <div className="md3-sheet-actions md3-sticky-bottom">
                    <button type="button" className="md3-btn-filled md3-btn-full" onClick={() => setFilterSheetOpen(false)}>
                      查看 {results.length} 条结果
                    </button>
                  </div>
                </>
              )}

              {activeFilterView === 'majorCategory' && renderNestedList(
                "选择大类",
                [{ value: "", label: "全部大类" }, ...majorCategories.map(c => ({ value: c.id, label: c.label }))],
                selectedMajorCategoryId,
                (val) => {
                  onSelectMajorCategoryId(val);
                  onSelectSubCategoryId("");
                }
              )}

              {activeFilterView === 'subCategory' && renderNestedList(
                "选择子类",
                [{ value: "", label: "全部子类" }, ...subcategories.map(c => ({ value: c.id, label: c.label }))],
                selectedSubCategoryId,
                onSelectSubCategoryId
              )}

              {activeFilterView === 'package' && renderNestedList(
                "选择封装",
                [{ value: "全部封装", label: "全部封装" }, ...packages.map(p => ({ value: p, label: p }))],
                selectedPackage,
                onSelectPackage
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
};
