import React from "react";
import type { ComponentMajorCategory, ComponentRecord } from "@shared/types";
import { useBottomSheetDragDismiss } from "./useBottomSheetDragDismiss";

const sourceTypeLabelMap: Record<string, string> = {
  manual: "手动录入",
  "csv-import": "CSV 导入",
};

interface ComponentDetailSheetProps {
  component: ComponentRecord | null;
  majorCategories: ComponentMajorCategory[];
  onClose: () => void;
}

export const ComponentDetailSheet: React.FC<ComponentDetailSheetProps> = ({ component, majorCategories, onClose }) => {
  const detailSheetDrag = useBottomSheetDragDismiss(onClose);

  if (!component) {
    return null;
  }

  const sourceTypeLabel = sourceTypeLabelMap[component.sourceType] || component.sourceType;
  const majorCategory = majorCategories.find((category) => category.id === component.majorCategoryId);
  const subCategory = majorCategory?.subcategories.find((category) => category.id === component.subCategoryId);

  return (
    <>
      <div className="md3-bottom-sheet-scrim open" onClick={onClose} style={detailSheetDrag.scrimStyle} />
      <div className={`md3-bottom-sheet open ${detailSheetDrag.isDragging ? "dragging" : ""}`} style={detailSheetDrag.sheetStyle}>
        <div className="md3-bottom-sheet-drag-handle" {...detailSheetDrag.dragHandleProps} />
        <div className="md3-bottom-sheet-content">
          <div className="md3-sheet-hero">
            <div>
              <h2 className="md3-sheet-title">{component.name}</h2>
              <div className="md3-sheet-subtitle">{component.partNumber}</div>
            </div>
            <span className={`md3-presence-pill ${component.owned ? "owned" : "missing"}`}>{component.owned ? "已持有" : "未持有"}</span>
          </div>

          <div className="md3-detail-attributes">
            <span className="md3-badge md3-badge-accent">大类: {majorCategory?.label || "未分类"}</span>
            <span className="md3-badge md3-badge-accent">子类: {subCategory?.label || component.category || "未分类"}</span>
            <span className="md3-badge">封装: {component.package}</span>
            <span className="md3-badge">位置: {component.location || "未存位置"}</span>
            {typeof component.quantity === "number" && component.quantity > 0 && (
              <span className="md3-badge">BOM 数量: {component.quantity}</span>
            )}
            <span className="md3-badge md3-badge-subtle">来源: {sourceTypeLabel}</span>
          </div>

          {Object.keys(component.attributes || {}).length > 0 && (
            <section className="md3-sheet-section">
              <h4>技术参数</h4>
              <div className="md3-detail-parameter-grid">
                {Object.entries(component.attributes).map(([key, value]) => (
                  <div key={key} className="md3-detail-kv-card"><strong>{key}</strong><span>{value}</span></div>
                ))}
              </div>
            </section>
          )}

          {(component.aliases.length > 0 || component.substitutes.length > 0 || component.keywords.length > 0 || (component.designators?.length ?? 0) > 0) && (
            <section className="md3-sheet-section">
              <h4>扩展信息</h4>
              <div className="md3-sheet-info-list">
                {(component.designators?.length ?? 0) > 0 && <p><strong>位号：</strong>{component.designators!.join(" / ")}</p>}
                {component.aliases.length > 0 && <p><strong>别名：</strong>{component.aliases.join(" / ")}</p>}
                {component.keywords.length > 0 && <p><strong>关键词：</strong>{component.keywords.join(" / ")}</p>}
                {component.substitutes.length > 0 && <p><strong>替代料：</strong>{component.substitutes.join(" / ")}</p>}
              </div>
            </section>
          )}

          <section className="md3-sheet-section">
            <h4>元件备注</h4>
            <div className="md3-detail-notes"><span>{component.notes || "暂无备注。"}</span></div>
          </section>

        </div>
      </div>

    </>
  );
};
