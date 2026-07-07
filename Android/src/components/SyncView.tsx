import React, { useRef, useState } from "react";
import { fetchSnapshotFromPc, normalizePcAddress } from "../sync-client";

interface SyncViewProps {
  statusText: string;
  onApplySnapshot: (jsonText: string) => void;
  onImportCsv: (csvText: string, strategy: "skip" | "merge" | "overwrite") => void;
}

interface SyncLogEntry {
  id: string;
  text: string;
}

export const SyncView: React.FC<SyncViewProps> = ({
  statusText,
  onApplySnapshot,
  onImportCsv,
}) => {
  const [pcAddress, setPcAddress] = useState(() => {
    return localStorage.getItem("sync_pc_address") || "http://192.168.1.5:10849";
  });
  const [pasteText, setPasteText] = useState("");
  const [csvText, setCsvText] = useState("");
  const [csvFileName, setCsvFileName] = useState("");
  const [syncLogs, setSyncLogs] = useState<SyncLogEntry[]>([]);
  const [csvValidationMessage, setCsvValidationMessage] = useState("");

  const fileInputRef = useRef<HTMLInputElement>(null);

  const pushSyncLog = (text: string) => {
    setSyncLogs((prev) => [
      ...prev,
      {
        id: `${Date.now()}-${prev.length}`,
        text,
      },
    ]);
  };

  const handleWifiSync = async () => {
    if (!pcAddress.trim()) {
      setSyncLogs([{ id: "pc-address-empty", text: "请输入 PC 端局域网服务地址" }]);
      return;
    }

    const normalizedAddress = normalizePcAddress(pcAddress);
    setSyncLogs([{ id: "sync-start", text: "正在尝试连接局域网 PC 端 API 服务..." }]);
    localStorage.setItem("sync_pc_address", normalizedAddress);
    setPcAddress(normalizedAddress);

    try {
      pushSyncLog("正在拉取元件数据库和分类列表...");
      const snapshot = await fetchSnapshotFromPc(normalizedAddress);
      onApplySnapshot(JSON.stringify(snapshot));
      pushSyncLog("局域网同步成功！");
      pushSyncLog(`已拉取 ${snapshot.meta.componentCount} 条元件记录及 ${snapshot.meta.categories.length} 个分类。`);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : "未知错误";
      pushSyncLog(`同步失败！原因: ${message}`);
      pushSyncLog("请检查电脑端 API 服务是否开启且处于同一 Wi-Fi。");
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) {
      return;
    }

    setCsvFileName(file.name);
    const reader = new FileReader();
    reader.onload = (event) => {
      const text = event.target?.result as string;
      setCsvText(text);
      setCsvValidationMessage("");
      setSyncLogs([
        {
          id: `csv-loaded-${file.name}`,
          text: `已成功读取本地 CSV 文件: ${file.name} (${text.length} 字符)。`,
        },
        {
          id: `csv-next-${file.name}`,
          text: "请在下方选择策略开始导入。",
        },
      ]);
    };
    reader.onerror = () => {
      setSyncLogs([{ id: `csv-failed-${file.name}`, text: `文件读取失败: ${file.name}` }]);
    };
    reader.readAsText(file);
  };

  const handleCsvImport = (strategy: "skip" | "merge" | "overwrite") => {
    if (!csvText.trim()) {
      setCsvValidationMessage("请先选择 CSV 文件或粘贴 CSV 文本。");
      return;
    }

    setCsvValidationMessage("");
    onImportCsv(csvText, strategy);
    setCsvText("");
    setCsvFileName("");
  };

  return (
    <div className="md3-sync-container">
      <header className="md3-top-app-bar md3-animate-enter">
        <div className="md3-top-app-bar__row md3-top-app-bar__row-sync">
          <div className="md3-sync-header">
            <h1>同步</h1>
          </div>
          <div className="md3-sync-status-chip" aria-label={`当前状态：${statusText}`}>
            <span className="md3-hero-status__label">当前状态</span>
            <strong>{statusText}</strong>
          </div>
        </div>
      </header>

      <section className="md3-sync-stack">
        <article className="status-card md3-sync-card md3-animate-enter" style={{ animationDelay: "220ms" }}>
          <div className="md3-card-head">
            <div>
              <h3>局域网</h3>
            </div>
          </div>
          <div className="md3-form-group">
            <label>PC 端地址</label>
            <input
              type="text"
              className="md3-input-field md3-input-field-compact"
              value={pcAddress}
              onChange={(e) => setPcAddress(e.target.value)}
              placeholder="http://192.168.1.100:10849"
              inputMode="url"
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
            />
          </div>
          <div className="md3-sync-action-row">
            <button type="button" className="md3-btn-filled md3-btn-inline" onClick={handleWifiSync}>
              立即连接并同步
            </button>
          </div>
          {syncLogs.length > 0 && (
            <div className="md3-log-panel">
              <div className="md3-log-panel__title">记录</div>
              <div className="md3-log-list">
                {syncLogs.map((entry) => (
                  <div key={entry.id} className="md3-log-item">
                    <span className="md3-log-item__dot" />
                    <span className="md3-log-item__text">{entry.text}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </article>

        <article className="status-card md3-sync-card md3-animate-enter" style={{ animationDelay: "260ms" }}>
          <div className="md3-card-head">
            <div>
              <h3>CSV 导入</h3>
            </div>
            <button type="button" className="md3-btn-outlined md3-btn-inline" onClick={() => fileInputRef.current?.click()}>
              选择文件
            </button>
            <input
              type="file"
              ref={fileInputRef}
              onChange={handleFileChange}
              accept=".csv"
              style={{ display: "none" }}
            />
          </div>
          {csvFileName && <div className="md3-inline-note">已选文件：{csvFileName}</div>}
          {csvValidationMessage && (
            <div className="md3-inline-banner md3-inline-banner-warning">
              {csvValidationMessage}
            </div>
          )}
          <div className="md3-form-group">
            <label>CSV 文本</label>
            <textarea
              className="md3-textarea-field md3-textarea-field-compact md3-sync-textarea"
              value={csvText}
              onChange={(e) => {
                setCsvText(e.target.value);
                if (csvValidationMessage) {
                  setCsvValidationMessage("");
                }
              }}
              placeholder="或者在这里直接粘贴 CSV 文本内容..."
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
            />
          </div>
          <div className="md3-form-group">
            <label>重复处理策略</label>
            <div className="md3-button-row md3-button-row-compact">
              <button type="button" className="md3-btn-outlined md3-btn-inline" onClick={() => handleCsvImport("skip")}>
                跳过重复
              </button>
              <button type="button" className="md3-btn-outlined md3-btn-inline" onClick={() => handleCsvImport("merge")}>
                合并别名
              </button>
              <button type="button" className="md3-btn-filled md3-btn-inline" onClick={() => handleCsvImport("overwrite")}>
                覆盖导入
              </button>
            </div>
          </div>
        </article>

        <article className="status-card md3-sync-card md3-animate-enter" style={{ animationDelay: "300ms" }}>
          <div className="md3-card-head">
            <div>
              <h3>JSON 导入</h3>
            </div>
          </div>
          <div className="md3-form-group">
            <label>Snapshot JSON</label>
            <textarea
              className="md3-textarea-field md3-textarea-field-compact md3-sync-textarea"
              value={pasteText}
              onChange={(e) => setPasteText(e.target.value)}
              placeholder="粘贴电脑端导出的 catalog.json 文件内容..."
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
            />
          </div>
          <div className="md3-sync-action-row">
            <button
              type="button"
              className="md3-btn-outlined md3-btn-inline"
              onClick={() => {
                if (pasteText.trim()) {
                  onApplySnapshot(pasteText.trim());
                  setPasteText("");
                }
              }}
            >
              导入 JSON 文本
            </button>
          </div>
        </article>
      </section>
    </div>
  );
};
