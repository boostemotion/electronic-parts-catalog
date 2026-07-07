import React from "react";
import type { ExportCatalogSnapshot } from "@shared/types";

interface AboutViewProps {
  snapshot: ExportCatalogSnapshot;
}

export const AboutView: React.FC<AboutViewProps> = ({ snapshot }) => {
  const ownedCount = snapshot.components.filter((component) => component.owned).length;

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
      <header className="md3-top-app-bar md3-animate-enter">
        <div className="md3-top-app-bar__row">
          <div>
            <h1>关于</h1>
          </div>
        </div>
      </header>

      <section className="md3-summary-grid md3-summary-grid-secondary">
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "40ms" }}>
          <span className="md3-summary-card__label">当前快照</span>
          <strong className="md3-summary-card__value">{snapshot.meta.componentCount}</strong>
        </article>
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "80ms" }}>
          <span className="md3-summary-card__label">已持有</span>
          <strong className="md3-summary-card__value">{ownedCount}</strong>
        </article>
        <article className="md3-summary-card md3-animate-enter" style={{ animationDelay: "120ms" }}>
          <span className="md3-summary-card__label">快照时间</span>
          <strong className="md3-summary-card__value md3-summary-card__value--tiny">{snapshotTime}</strong>
        </article>
      </section>

      <article className="status-card md3-animate-enter" style={{ animationDelay: "300ms" }}>
        <div className="md3-card-head">
          <div>
            <h3>数据状态</h3>
          </div>
          <span className="md3-badge">{snapshot.meta.categories.length} 个分类</span>
        </div>
        <div className="md3-info-list">
          <div className="md3-info-row">
            <span>元件总计</span>
            <strong>{snapshot.meta.componentCount} 个</strong>
          </div>
          <div className="md3-info-row">
            <span>最近快照时间</span>
            <strong>{snapshotTime}</strong>
          </div>
          <div className="md3-info-row">
            <span>目录特征</span>
            <strong>中文 / 英文 / 型号 / 封装</strong>
          </div>
        </div>
      </article>

      <div className="md3-footer-note">电子元件入库台 Android 查询端</div>
    </div>
  );
};
