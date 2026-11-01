import type { AvailableAppUpdate } from "../app-update";
import "./AppUpdatePrompt.css";

interface AppUpdatePromptProps {
  update: AvailableAppUpdate | null;
  isInstalling: boolean;
  onLater: () => void;
  onUpdate: () => void;
}

function formatApkSize(sizeBytes: number): string {
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function AppUpdatePrompt({ update, isInstalling, onLater, onUpdate }: AppUpdatePromptProps) {
  if (!update) {
    return null;
  }

  return (
    <div className="md3-update-scrim">
      <section className="md3-update-prompt" role="dialog" aria-modal="true" aria-labelledby="app-update-title">
        <div className="md3-update-prompt__hero" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 3v12" />
            <path d="m7.5 10.5 4.5 4.5 4.5-4.5" />
            <path d="M5 19h14" />
          </svg>
        </div>
        <div className="md3-update-prompt__content">
          <span className="md3-update-prompt__eyebrow">ANDROID 更新</span>
          <h2 id="app-update-title">发现新版本</h2>
          <p className="md3-update-prompt__version">版本 {update.manifest.versionName}<span aria-hidden="true"> · </span>{formatApkSize(update.manifest.sizeBytes)}</p>
          {update.manifest.releaseNotes && <p className="md3-update-prompt__notes">{update.manifest.releaseNotes}</p>}
          <p className="md3-update-prompt__hint">下载并校验后，将由 Android 系统安装器确认更新。</p>
        </div>
        <div className="md3-update-prompt__actions">
          <button type="button" className="md3-update-button md3-update-button--text" onClick={onLater} disabled={isInstalling}>稍后</button>
          <button type="button" className="md3-update-button md3-update-button--filled" onClick={onUpdate} disabled={isInstalling}>
            {isInstalling ? "正在准备安装…" : "更新"}
          </button>
        </div>
      </section>
    </div>
  );
}
