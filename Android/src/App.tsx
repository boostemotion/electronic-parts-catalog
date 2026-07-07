import { startTransition, useDeferredValue, useEffect, useState } from "react";

import type { ComponentRecord, ExportCatalogSnapshot, SearchResult } from "@shared/types";
import {
  loadSnapshot,
  saveSnapshot,
  searchSnapshot,
  addLocalComponent,
  updateLocalComponent,
  deleteLocalComponent,
  generateLocalId,
  importCsvToSnapshot,
} from "./snapshot-store";
import { deleteComponentFromPc, upsertComponentToPc } from "./sync-client";
import "./App.css";

// 引入 Material 3 页面子组件
import { CatalogView } from "./components/CatalogView";
import { SyncView } from "./components/SyncView";
import { AboutView } from "./components/AboutView";
import { ComponentDetailSheet } from "./components/ComponentDetailSheet";
import { ComponentEditSheet } from "./components/ComponentEditSheet";
import { AppIcon } from "./components/AppIcon";
import { AppSnackbar } from "./components/AppSnackbar";

type ActiveTab = "catalog" | "sync" | "about";
type NavDirection = "none" | "forward" | "backward";

const tabOrder: ActiveTab[] = ["catalog", "sync", "about"];

function App() {
  const [snapshot, setSnapshot] = useState<ExportCatalogSnapshot>(() => loadSnapshot());
  const [selectedCategory, setSelectedCategory] = useState("全部");
  const [searchText, setSearchText] = useState("");
  const deferredSearchText = useDeferredValue(searchText);
  
  // 新增：高级结构化筛选状态
  const [selectedPackage, setSelectedPackage] = useState("全部封装");
  const [selectedOwned, setSelectedOwned] = useState("全部");
  const [attributeFilter, setAttributeFilter] = useState("");
  const deferredAttributeFilter = useDeferredValue(attributeFilter);

  const [results, setResults] = useState<SearchResult[]>([]);
  const [selectedComponent, setSelectedComponent] = useState<ComponentRecord | null>(null);
  const [statusText, setStatusText] = useState("已读取本地离线快照");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  
  // 新增：编辑对话框控制状态
  const [editingComponent, setEditingComponent] = useState<ComponentRecord | null>(null);
  const [editOpen, setEditOpen] = useState(false);

  // 当前活动 Tab 页
  const [activeTab, setActiveTab] = useState<ActiveTab>("catalog");
  const [navDirection, setNavDirection] = useState<NavDirection>("none");

  useEffect(() => {
    const filtered = searchSnapshot(snapshot, {
      text: deferredSearchText,
      category: selectedCategory,
      package: selectedPackage === "全部封装" ? undefined : selectedPackage,
      owned: selectedOwned === "全部" ? undefined : selectedOwned === "仅持有",
      attributeText: deferredAttributeFilter || undefined,
    });
    setResults(filtered);
  }, [deferredSearchText, selectedCategory, selectedPackage, selectedOwned, deferredAttributeFilter, snapshot]);

  useEffect(() => {
    if (!snackbarVisible) {
      return;
    }

    const timer = window.setTimeout(() => {
      setSnackbarVisible(false);
    }, 2600);

    return () => {
      window.clearTimeout(timer);
    };
  }, [snackbarVisible, snackbarMessage]);

  // 从元件列表中提取所有的唯一封装列表
  const uniquePackages = Array.from(
    new Set(snapshot.components.map((c) => c.package).filter(Boolean))
  ).sort();

  const announceStatus = (message: string) => {
    setStatusText(message);
    setSnackbarMessage(message);
    setSnackbarVisible(true);
  };

  function applySnapshot(text: string): void {
    try {
      const parsed = JSON.parse(text) as ExportCatalogSnapshot;
      saveSnapshot(parsed);
      announceStatus(`导入成功 (${parsed.meta.componentCount} 条元件)`);
      startTransition(() => {
        setSnapshot(parsed);
        setSelectedCategory("全部");
        setSelectedPackage("全部封装");
        setSelectedOwned("全部");
        setAttributeFilter("");
        setSearchText("");
      });
    } catch {
      announceStatus("导入 JSON 格式错误，请重新获取");
    }
  }

  // 1. 新增元件入口
  const handleNewComponent = () => {
    setEditingComponent(null);
    setEditOpen(true);
  };

  // 2. 选中元件进行编辑
  const handleEditComponent = (component: ComponentRecord) => {
    setSelectedComponent(null); // 关闭详情 Bottom Sheet
    setEditingComponent(component);
    setEditOpen(true);
  };

  // 3. 保存元件事件（支持本地快照更新及 PC 远程 API 写入）
  const handleSaveComponent = async (component: ComponentRecord) => {
    setEditOpen(false);
    let updatedSnapshot = snapshot;

    if (!component.id) {
      // 新建空白元件
      const generatedId = generateLocalId(snapshot, component.name, component.partNumber);
      component.id = generatedId;
      updatedSnapshot = addLocalComponent(snapshot, component);
    } else {
      // 修改元件
      updatedSnapshot = updateLocalComponent(snapshot, component);
    }

    announceStatus(`已保存元件: ${component.name}`);
    startTransition(() => {
      setSnapshot(updatedSnapshot);
    });

    // 对接 PC 远程 API (若是联机状态)
    const pcAddress = localStorage.getItem("sync_pc_address");
    if (pcAddress) {
      try {
        await upsertComponentToPc(pcAddress, component);
        announceStatus(`已同步修改至 PC: ${component.name}`);
      } catch (err) {
        console.warn("未连接局域网 PC，已仅写入本地离线快照", err);
        announceStatus(`已保存本地，PC 同步失败: ${err instanceof Error ? err.message : "未知错误"}`);
      }
    }
  };

  // 4. 删除元件事件（支持本地删除及 PC 远程 API 同步删除）
  const handleDeleteComponent = async (id: string) => {
    setSelectedComponent(null);
    const updatedSnapshot = deleteLocalComponent(snapshot, id);
    announceStatus("已删除该元件记录");
    startTransition(() => {
      setSnapshot(updatedSnapshot);
    });

    // 对接 PC 远程 API
    const pcAddress = localStorage.getItem("sync_pc_address");
    if (pcAddress) {
      try {
        await deleteComponentFromPc(pcAddress, id);
        announceStatus("已同步删除 PC 端元件");
      } catch (err) {
        console.warn("未连接局域网 PC，已仅从本地快照中移除", err);
        announceStatus(`已删除本地，PC 同步失败: ${err instanceof Error ? err.message : "未知错误"}`);
      }
    }
  };

  // 5. 导入 CSV 批量入库
  const handleImportCsv = (csvText: string, strategy: "skip" | "merge" | "overwrite") => {
    const res = importCsvToSnapshot(snapshot, csvText, strategy);
    let message = "";
    if (res.errorCount > 0 && res.validCount === 0) {
      message = `导入失败: ${res.firstError}`;
    } else {
      message = `成功解析导入 ${res.validCount} 条元件`;
      if (res.errorCount > 0) {
        message += `，忽略错误行 ${res.errorCount} 条（${res.firstError}）`;
      }
    }

    announceStatus(message);
    startTransition(() => {
      setSnapshot(res.snapshot);
    });
  };

  const switchTab = (nextTab: ActiveTab) => {
    if (nextTab === activeTab) {
      return;
    }

    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }

    const currentIndex = tabOrder.indexOf(activeTab);
    const nextIndex = tabOrder.indexOf(nextTab);
    setNavDirection(nextIndex > currentIndex ? "forward" : "backward");
    setActiveTab(nextTab);
  };

  return (
    <div className="android-app-container">
      {/* 核心内容视口 */}
      <div
        className={`android-content-area tab-${activeTab}`}
        data-active-tab={activeTab}
        data-nav-direction={navDirection}
      >
        {activeTab === "catalog" && (
          <CatalogView
            snapshot={snapshot}
            categories={snapshot.meta.categories}
            results={results}
            selectedCategory={selectedCategory}
            onSelectCategory={setSelectedCategory}
            searchText={searchText}
            onSearchChange={setSearchText}
            onSelectComponent={setSelectedComponent}
            
            packages={uniquePackages}
            selectedPackage={selectedPackage}
            onSelectPackage={setSelectedPackage}
            selectedOwned={selectedOwned}
            onSelectOwned={setSelectedOwned}
            attributeFilter={attributeFilter}
            onAttributeFilterChange={setAttributeFilter}
            onNewComponent={handleNewComponent}
            onOpenSync={() => switchTab("sync")}
          />
        )}

        {activeTab === "sync" && (
          <SyncView
            statusText={statusText}
            onApplySnapshot={applySnapshot}
            onImportCsv={handleImportCsv}
          />
        )}

        {activeTab === "about" && (
          <AboutView snapshot={snapshot} />
        )}
      </div>

      {/* Material 3 滑入式详情抽屉 */}
      <ComponentDetailSheet
        component={selectedComponent}
        onClose={() => setSelectedComponent(null)}
        onEdit={handleEditComponent}
        onDelete={handleDeleteComponent}
      />

      {/* Material 3 滑入式编辑/新增抽屉 */}
      <ComponentEditSheet
        component={editingComponent}
        isOpen={editOpen}
        onClose={() => setEditOpen(false)}
        onSave={handleSaveComponent}
      />

      {/* Material 3 底部导航栏 */}
      <nav className="md3-bottom-nav">
        <button
          type="button"
          className={`md3-nav-item ${activeTab === "catalog" ? "active" : ""}`}
          onClick={() => switchTab("catalog")}
          aria-current={activeTab === "catalog" ? "page" : undefined}
          aria-label="元件目录"
        >
          <div className="md3-nav-indicator">
            <AppIcon name="catalog" className="md3-nav-icon" />
          </div>
          <span>元件目录</span>
        </button>

        <button
          type="button"
          className={`md3-nav-item ${activeTab === "sync" ? "active" : ""}`}
          onClick={() => switchTab("sync")}
          aria-current={activeTab === "sync" ? "page" : undefined}
          aria-label="数据同步"
        >
          <div className="md3-nav-indicator">
            <AppIcon name="sync" className="md3-nav-icon" />
          </div>
          <span>数据同步</span>
        </button>

        <button
          type="button"
          className={`md3-nav-item ${activeTab === "about" ? "active" : ""}`}
          onClick={() => switchTab("about")}
          aria-current={activeTab === "about" ? "page" : undefined}
          aria-label="关于"
        >
          <div className="md3-nav-indicator">
            <AppIcon name="about" className="md3-nav-icon" />
          </div>
          <span>关于</span>
        </button>
      </nav>

      <AppSnackbar message={snackbarMessage} isVisible={snackbarVisible} />
    </div>
  );
}

export default App;
