import React from "react";
import type { ExportCatalogSnapshot } from "@shared/types";
import { CURRENT_APP_VERSION_CODE, CURRENT_APP_VERSION_NAME } from "../app-update";

interface AboutViewProps {
  snapshot: ExportCatalogSnapshot;
  syncSource?: string;
  lastCheckText?: string;
  updateStatus?: string;
}

export const AboutView: React.FC<AboutViewProps> = ({
  snapshot,
  syncSource = "本地离线快照",
  lastCheckText = "尚未检查",
  updateStatus = "尚未检查更新",
}) => {
  const ownedCount = snapshot.components.filter((component) => component.owned).length;
  const taxonomyVersion = snapshot.meta.taxonomyVersion || "未知";

  const formatSnapshotTime = (isoString?: string) => {
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
  };

  const snapshotTime = formatSnapshotTime(snapshot.meta.exportedAt);

  return (
    <div className="md3-sync-container">
      <header className="md3-about-header md3-animate-enter">
        <div className="md3-about-identity">
          <h1>关于</h1>
          <p className="md3-about-subtitle">电子元件入库台 Android 查询端</p>
        </div>
        <div className="md3-about-version-chip">
          v{CURRENT_APP_VERSION_NAME} (code {CURRENT_APP_VERSION_CODE})
        </div>
      </header>

      <section className="status-card md3-about-dashboard md3-animate-enter">
        <div className="md3-card-head">
          <h3>数据状态</h3>
        </div>

        <div className="md3-dashboard-metrics">
          <div className="metric-item">
            <span className="metric-label">元件总计</span>
            <strong className="metric-value">{snapshot.meta.componentCount} 个</strong>
          </div>
          <div className="metric-item">
            <span className="metric-label">已持有</span>
            <strong className="metric-value">{ownedCount} 个</strong>
          </div>
          <div className="metric-item">
            <span className="metric-label">分类数量</span>
            <strong className="metric-value">{snapshot.meta.categories.length} 个</strong>
          </div>
          <div className="metric-item">
            <span className="metric-label">目录分类版本</span>
            <strong className="metric-value">v{taxonomyVersion}</strong>
          </div>
        </div>

        <div className="about-details-list">
          <div className="about-detail-row">
            <span className="about-detail-label">快照时间</span>
            <strong className="about-detail-value">{snapshotTime}</strong>
          </div>
          <div className="about-detail-row">
            <span className="about-detail-label">同步来源</span>
            <strong className="about-detail-value">{syncSource}</strong>
          </div>
          <div className="about-detail-row">
            <span className="about-detail-label">上次检查</span>
            <strong className="about-detail-value">{lastCheckText}</strong>
          </div>
          <div className="about-detail-row">
            <span className="about-detail-label">应用更新</span>
            <strong className="about-detail-value">{updateStatus}</strong>
          </div>
        </div>
      </section>
    </div>
  );
};
