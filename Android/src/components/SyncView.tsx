import React from "react";
import type { ExportCatalogSnapshot } from "@shared/types";

export type SyncState = "idle" | "discovering" | "syncing" | "synced" | "offline" | "error";

interface SyncViewProps {
  snapshot: ExportCatalogSnapshot;
  syncState: SyncState;
  statusText: string;
  onRetry: () => void;
  syncSource?: string;
  lastCheckText?: string;
  lastSuccessAt?: string | null;
  updateStatus?: string;
  onRetryUpdate?: () => void;
}

function formatSnapshotTime(isoString?: string): string {
  if (!isoString) {
    return "未同步";
  }

  return new Intl.DateTimeFormat("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(new Date(isoString));
}

function getStateLabel(syncState: SyncState): string {
  switch (syncState) {
    case "discovering":
      return "正在查找";
    case "syncing":
      return "正在同步";
    case "synced":
      return "已同步";
    case "offline":
      return "离线";
    case "error":
      return "已离线";
    default:
      return "本地快照";
  }
}

export const SyncView: React.FC<SyncViewProps> = ({
  snapshot,
  syncState,
  statusText,
  onRetry,
  syncSource = "本地离线快照",
  lastCheckText = "尚未检查",
  lastSuccessAt = null,
  updateStatus = "尚未检查更新",
  onRetryUpdate,
}) => {
  const taxonomyVersion = snapshot.meta.taxonomyVersion || "未知";
  const isBusy = syncState === "discovering" || syncState === "syncing";
  const isOfflineOrError = syncState === "offline" || syncState === "error";
  const stateLabel = getStateLabel(syncState);
  const isSynced = syncState === "synced";
  const showUpdateRetry = Boolean(onRetryUpdate) && updateStatus.includes("失败");

  const showStatusNotice = (isBusy || isOfflineOrError) && statusText && !statusText.includes("已同步") && !statusText.includes("刚刚");

  return (
    <div className="md3-sync-container">
      <header className="md3-top-app-bar md3-animate-enter">
        <div className="md3-top-app-bar__row md3-top-app-bar__row-sync">
          <div className="md3-sync-header"><h1>同步</h1></div>
          <div className="md3-sync-status-chip" aria-label={`当前状态：${stateLabel}`}>
            <strong>{stateLabel}</strong>
          </div>
        </div>
      </header>

      <article className="status-card md3-sync-dashboard md3-animate-enter">
        <div className="md3-card-head">
          <h3>Windows 自动同步</h3>
        </div>

        <div className="md3-dashboard-metrics">
          <div className="metric-item">
            <span className="metric-label">元件总数</span>
            <strong className="metric-value">{snapshot.meta.componentCount} 元件</strong>
          </div>
          <div className="metric-item">
            <span className="metric-label">分类版本</span>
            <strong className="metric-value">v{taxonomyVersion}</strong>
          </div>
          <div className="metric-item">
            <span className="metric-label">最后同步</span>
            <strong className="metric-value">{formatSnapshotTime(snapshot.meta.exportedAt)}</strong>
          </div>
        </div>

        <div className="sync-diagnostic-list">
          <div className="sync-diagnostic-row">
            <span className="sync-diagnostic-label">同步来源</span>
            <strong className="sync-diagnostic-value">{syncSource}</strong>
          </div>
          <div className="sync-diagnostic-row">
            <span className="sync-diagnostic-label">上次检查</span>
            <strong className="sync-diagnostic-value">{lastCheckText}</strong>
          </div>
          <div className="sync-diagnostic-row">
            <span className="sync-diagnostic-label">设备同步</span>
            <strong className="sync-diagnostic-value">{lastSuccessAt ? formatSnapshotTime(lastSuccessAt) : "尚未成功"}</strong>
          </div>
          <div className="sync-diagnostic-row">
            <span className="sync-diagnostic-label">应用更新</span>
            <strong className="sync-diagnostic-value">{updateStatus}</strong>
          </div>
        </div>

        {showStatusNotice && (
          <div className="sync-status-notice" role="status" aria-live="polite">{statusText}</div>
        )}

        {showUpdateRetry && (
          <button type="button" className="md3-btn-outlined md3-btn-full-width" onClick={onRetryUpdate}>
            重新检查更新
          </button>
        )}

        <button
          type="button"
          className="md3-btn-filled md3-btn-full-width"
          onClick={onRetry}
          disabled={isBusy}
        >
          {isBusy ? "正在查找..." : "重新查找电脑"}
        </button>

        {!isSynced && (
          <p className="sync-wifi-guidance">请在同一 Wi-Fi 下开启 Windows 同步端</p>
        )}
      </article>
    </div>
  );
};
