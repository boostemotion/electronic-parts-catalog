import React, { useEffect, useState } from "react";
import type { ComponentRecord } from "@shared/types";
import { parseAttributes } from "@shared/catalog";
import { useBottomSheetDragDismiss } from "./useBottomSheetDragDismiss";

interface ComponentEditSheetProps {
  component: ComponentRecord | null;
  isOpen: boolean;
  onClose: () => void;
  onSave: (component: ComponentRecord) => void;
}

export const ComponentEditSheet: React.FC<ComponentEditSheetProps> = ({
  component,
  isOpen,
  onClose,
  onSave,
}) => {
  const [name, setName] = useState("");
  const [partNumber, setPartNumber] = useState("");
  const [category, setCategory] = useState("未分类");
  const [packageName, setPackageName] = useState("");
  const [location, setLocation] = useState("");
  const [owned, setOwned] = useState(true);
  const [aliasesText, setAliasesText] = useState("");
  const [keywordsText, setKeywordsText] = useState("");
  const [substitutesText, setSubstitutesText] = useState("");
  const [attributesText, setAttributesText] = useState("");
  const [notes, setNotes] = useState("");
  const [validationMessage, setValidationMessage] = useState("");

  useEffect(() => {
    if (component) {
      setName(component.name);
      setPartNumber(component.partNumber);
      setCategory(component.category);
      setPackageName(component.package);
      setLocation(component.location);
      setOwned(component.owned);
      setAliasesText(component.aliases.join(", "));
      setKeywordsText(component.keywords.join(", "));
      setSubstitutesText(component.substitutes.join(", "));
      setAttributesText(
        Object.entries(component.attributes)
          .map(([k, v]) => `${k}=${v}`)
          .join("; ")
      );
      setNotes(component.notes);
      setValidationMessage("");
      return;
    }

    setName("");
    setPartNumber("");
    setCategory("未分类");
    setPackageName("");
    setLocation("");
    setOwned(true);
    setAliasesText("");
    setKeywordsText("");
    setSubstitutesText("");
    setAttributesText("");
    setNotes("");
    setValidationMessage("");
  }, [component, isOpen]);

  const editSheetDrag = useBottomSheetDragDismiss(onClose);

  if (!isOpen) {
    return null;
  }

  const handleSave = () => {
    if (!name.trim()) {
      setValidationMessage("请输入元件名称");
      return;
    }
    if (!partNumber.trim()) {
      setValidationMessage("请输入元件型号");
      return;
    }

    setValidationMessage("");

    const splitList = (txt: string) =>
      txt
        .split(/[,,，|]/)
        .map((x) => x.trim())
        .filter(Boolean);

    onSave({
      id: component?.id || "",
      name: name.trim(),
      partNumber: partNumber.trim(),
      category: category.trim(),
      package: packageName.trim(),
      location: location.trim(),
      owned,
      aliases: splitList(aliasesText),
      keywords: splitList(keywordsText),
      substitutes: splitList(substitutesText),
      attributes: parseAttributes(attributesText),
      notes: notes.trim(),
      sourceType: component?.sourceType || "manual",
    });
  };

  return (
    <>
      <div className="md3-bottom-sheet-scrim open" onClick={onClose} style={editSheetDrag.scrimStyle} />
      <div
        className={`md3-bottom-sheet md3-editor-sheet open ${editSheetDrag.isDragging ? "dragging" : ""}`}
        style={editSheetDrag.sheetStyle}
      >
        <div className="md3-bottom-sheet-drag-handle" {...editSheetDrag.dragHandleProps} />

        <div className="md3-bottom-sheet-content">
          <div className="md3-sheet-header">
            <div>
              <h2 className="md3-sheet-title">{component ? "编辑元件" : "新建元件"}</h2>
            </div>
          </div>

          {validationMessage && (
            <div className="md3-inline-banner md3-inline-banner-danger">
              {validationMessage}
            </div>
          )}

          <div className="md3-editor-grid">
            <div className="md3-form-group">
              <label>名称</label>
              <input
                type="text"
                className="md3-input-field"
                value={name}
                onChange={(e) => {
                  setName(e.target.value);
                  if (validationMessage) {
                    setValidationMessage("");
                  }
                }}
                placeholder="如：稳压芯片"
                spellCheck={false}
              />
            </div>

            <div className="md3-form-group">
              <label>型号</label>
              <input
                type="text"
                className="md3-input-field"
                value={partNumber}
                onChange={(e) => {
                  setPartNumber(e.target.value);
                  if (validationMessage) {
                    setValidationMessage("");
                  }
                }}
                placeholder="如：AMS1117-3.3"
                autoCapitalize="off"
                autoCorrect="off"
                spellCheck={false}
              />
            </div>

            <div className="md3-form-group">
              <label>分类</label>
              <input type="text" className="md3-input-field" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="如：电源" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>封装</label>
              <input type="text" className="md3-input-field" value={packageName} onChange={(e) => setPackageName(e.target.value)} placeholder="如：SOT223" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>位置</label>
              <input type="text" className="md3-input-field" value={location} onChange={(e) => setLocation(e.target.value)} placeholder="如：A柜抽屉 B1" spellCheck={false} />
            </div>

            <div className="md3-toggle-row">
              <label>持有状态</label>
              <button
                type="button"
                className={`md3-switch ${owned ? "on" : ""}`}
                aria-label="切换持有状态"
                onClick={() => setOwned((prev) => !prev)}
              >
                <span />
              </button>
            </div>

            <div className="md3-form-group">
              <label>别名</label>
              <input type="text" className="md3-input-field" value={aliasesText} onChange={(e) => setAliasesText(e.target.value)} placeholder="LDO 3v3, AMS1117" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>关键词</label>
              <input type="text" className="md3-input-field" value={keywordsText} onChange={(e) => setKeywordsText(e.target.value)} placeholder="电源, 降压, 3.3v" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>替代料</label>
              <input type="text" className="md3-input-field" value={substitutesText} onChange={(e) => setSubstitutesText(e.target.value)} placeholder="XC6206, ME6211" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>技术参数</label>
              <input type="text" className="md3-input-field" value={attributesText} onChange={(e) => setAttributesText(e.target.value)} placeholder="voltage=3.3V; current=800mA" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
            </div>

            <div className="md3-form-group">
              <label>备注</label>
              <textarea className="md3-textarea-field md3-textarea-field-compact" value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="添加补充备注说明..." spellCheck={false} />
            </div>
          </div>

          <div className="md3-sheet-actions md3-sheet-actions-split">
            <button type="button" className="md3-btn-outlined" onClick={onClose}>
              取消
            </button>
            <button type="button" className="md3-btn-filled" onClick={handleSave}>
              保存修改
            </button>
          </div>
        </div>
      </div>
    </>
  );
};
