import React from "react";
import type { ComponentRecord } from "@shared/types";
import { useBottomSheetDragDismiss } from "./useBottomSheetDragDismiss";
import { AppDialog } from "./AppDialog";

const sourceTypeLabelMap: Record<string, string> = {
  manual: "手动录入",
  "csv-import": "CSV 导入",
};

interface ComponentDetailSheetProps {
  component: ComponentRecord | null;
  onClose: () => void;
  onEdit: (component: ComponentRecord) => void;
  onDelete: (id: string) => void;
}

export const ComponentDetailSheet: React.FC<ComponentDetailSheetProps> = ({
  component,
  onClose,
  onEdit,
  onDelete,
}) => {
  const detailSheetDrag = useBottomSheetDragDismiss(onClose);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = React.useState(false);

  if (!component) {
    return null;
  }

  const handleDeleteClick = () => {
    setConfirmDeleteOpen(true);
  };

  const sourceTypeLabel = sourceTypeLabelMap[component.sourceType] || component.sourceType;

  return (
    <>
      <div
        className="md3-bottom-sheet-scrim open"
        onClick={onClose}
        style={detailSheetDrag.scrimStyle}
      />

      <div
        className={`md3-bottom-sheet open ${detailSheetDrag.isDragging ? "dragging" : ""}`}
        style={detailSheetDrag.sheetStyle}
      >
        <div className="md3-bottom-sheet-drag-handle" {...detailSheetDrag.dragHandleProps} />
        <div className="md3-bottom-sheet-content">
          <div className="md3-sheet-hero">
            <div>
              <h2 className="md3-sheet-title">{component.name}</h2>
              <div className="md3-sheet-subtitle">{component.partNumber}</div>
            </div>
            <span className={`md3-presence-pill ${component.owned ? "owned" : "missing"}`}>
              {component.owned ? "已持有" : "未持有"}
            </span>
          </div>

          <div className="md3-detail-attributes">
            <span className="md3-badge md3-badge-accent">分类: {component.category}</span>
            <span className="md3-badge">封装: {component.package}</span>
            <span className="md3-badge">位置: {component.location || "未存位置"}</span>
            <span className="md3-badge md3-badge-subtle">
              来源: {sourceTypeLabel}
            </span>
          </div>

          {Object.keys(component.attributes || {}).length > 0 && (
            <section className="md3-sheet-section">
              <h4>技术参数</h4>
              <div className="md3-detail-parameter-grid">
                {Object.entries(component.attributes).map(([key, value]) => (
                  <div key={key} className="md3-detail-kv-card">
                    <strong>{key}</strong>
                    <span>{value}</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {(component.aliases.length > 0 || component.substitutes.length > 0 || component.keywords.length > 0) && (
            <section className="md3-sheet-section">
              <h4>扩展信息</h4>
              <div className="md3-sheet-info-list">
                {component.aliases.length > 0 && <p><strong>别名：</strong>{component.aliases.join(" / ")}</p>}
                {component.keywords.length > 0 && <p><strong>关键词：</strong>{component.keywords.join(" / ")}</p>}
                {component.substitutes.length > 0 && <p><strong>替代料：</strong>{component.substitutes.join(" / ")}</p>}
              </div>
            </section>
          )}

          <section className="md3-sheet-section">
            <h4>元件备注</h4>
            <div className="md3-detail-notes">
              <span>{component.notes || "暂无备注。"}</span>
            </div>
          </section>

          <div className="md3-sheet-actions md3-sheet-actions-split">
            <button
              type="button"
              className="md3-btn-filled"
              onClick={() => onEdit(component)}
            >
              修改属性
            </button>
            <button
              type="button"
              className="md3-btn-outlined md3-btn-danger"
              onClick={handleDeleteClick}
            >
              删除元件
            </button>
          </div>
        </div>
      </div>

      <AppDialog
        isOpen={confirmDeleteOpen}
        title="确认删除元件"
        description={`将要删除 ${component.name}（${component.partNumber}）。这个操作会同时影响本地目录，若已连接 PC 端还会尝试同步删除。`}
        confirmLabel="确认删除"
        cancelLabel="暂不删除"
        tone="danger"
        onCancel={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          onDelete(component.id);
        }}
      />
    </>
  );
};
