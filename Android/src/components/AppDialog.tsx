import React from "react";

interface AppDialogProps {
  isOpen: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel?: string;
  tone?: "default" | "danger";
  onCancel: () => void;
  onConfirm: () => void;
}

export const AppDialog: React.FC<AppDialogProps> = ({
  isOpen,
  title,
  description,
  confirmLabel,
  cancelLabel = "取消",
  tone = "default",
  onCancel,
  onConfirm,
}) => {
  if (!isOpen) {
    return null;
  }

  return (
    <>
      <div className="md3-dialog-scrim" onClick={onCancel} />
      <div className="md3-dialog" role="dialog" aria-modal="true" aria-labelledby="app-dialog-title">
        <div className="md3-dialog__content">
          <h2 id="app-dialog-title" className="md3-dialog__title">{title}</h2>
          <p className="md3-dialog__description">{description}</p>
        </div>
        <div className="md3-dialog__actions">
          <button type="button" className="md3-btn-outlined" onClick={onCancel}>
            {cancelLabel}
          </button>
          <button
            type="button"
            className={`md3-btn-filled ${tone === "danger" ? "md3-btn-filled-danger" : ""}`}
            onClick={onConfirm}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </>
  );
};
