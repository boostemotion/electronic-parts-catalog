import { startTransition, useCallback, useDeferredValue, useEffect, useRef, useState } from "react";

import type { ComponentRecord, ExportCatalogSnapshot, SearchResult } from "@shared/types";
import { sortSearchResults, type SortOrder } from "./catalog-sort";
import { loadSnapshot, saveSnapshot, searchSnapshot } from "./snapshot-store";
import { discoverWindowsCatalog, fetchSnapshotFromPc, restoreDiscoveredCatalog } from "./sync-client";
import { fetchAvailableAppUpdate, type AvailableAppUpdate } from "./app-update";
import { downloadAndInstallUpdate } from "./app-update-plugin";
import "./App.css";
import "./motion.css";

import { CatalogView } from "./components/CatalogView";
import { SyncView, type SyncState } from "./components/SyncView";
import { AboutView } from "./components/AboutView";
import { ComponentDetailSheet } from "./components/ComponentDetailSheet";
import { AppIcon } from "./components/AppIcon";
import { AppSnackbar } from "./components/AppSnackbar";
import { AppUpdatePrompt } from "./components/AppUpdatePrompt";

type ActiveTab = "catalog" | "sync" | "about";
type NavDirection = "none" | "forward" | "backward";

const LAST_DISCOVERY_URL_KEY = "sync_last_discovery_url";
const LAST_SUCCESS_AT_KEY = "sync_last_success_at";
const DISMISSED_UPDATE_VERSION_KEY = "app_update_dismissed_version_code";
const LEGACY_SYNC_KEYS = ["sync_pc_address", "sync_pc_token"];
const FOREGROUND_REFRESH_INTERVAL_MS = 5 * 60 * 1000;
const tabOrder: ActiveTab[] = ["catalog", "sync", "about"];

function readDismissedUpdateVersionCode(): number | null {
  const raw = window.localStorage.getItem(DISMISSED_UPDATE_VERSION_KEY);
  const parsed = raw ? Number(raw) : Number.NaN;
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}

function App() {
  const [snapshot, setSnapshot] = useState<ExportCatalogSnapshot>(() => loadSnapshot());
  const [selectedMajorCategoryId, setSelectedMajorCategoryId] = useState("");
  const [selectedSubCategoryId, setSelectedSubCategoryId] = useState("");
  const [searchText, setSearchText] = useState("");
  const deferredSearchText = useDeferredValue(searchText);
  const [selectedPackage, setSelectedPackage] = useState("全部封装");
  const [selectedOwned, setSelectedOwned] = useState("全部");
  const [attributeFilter, setAttributeFilter] = useState("");
  const deferredAttributeFilter = useDeferredValue(attributeFilter);
  const [sortOrder, setSortOrder] = useState<SortOrder>("relevance");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [selectedComponent, setSelectedComponent] = useState<ComponentRecord | null>(null);
  const [syncState, setSyncState] = useState<SyncState>("idle");
  const [statusText, setStatusText] = useState("已读取本地离线快照");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const [snackbarVisible, setSnackbarVisible] = useState(false);
  const [activeTab, setActiveTab] = useState<ActiveTab>("catalog");
  const [navDirection, setNavDirection] = useState<NavDirection>("none");
  const [availableAppUpdate, setAvailableAppUpdate] = useState<AvailableAppUpdate | null>(null);
  const [isInstallingUpdate, setIsInstallingUpdate] = useState(false);
  const [syncSource, setSyncSource] = useState("本地离线快照");
  const [lastCheckText, setLastCheckText] = useState("尚未检查");
  const [lastSuccessAt, setLastSuccessAt] = useState<string | null>(() => window.localStorage.getItem(LAST_SUCCESS_AT_KEY));
  const [updateStatus, setUpdateStatus] = useState("尚未检查更新");
  const lastSuccessfulSyncRef = useRef(0);
  const refreshGenerationRef = useRef(0);

  const majorCategories = snapshot.meta.majorCategories;

  useEffect(() => {
    const searchResults = searchSnapshot(snapshot, {
      text: deferredSearchText,
      category: "全部",
      majorCategoryId: selectedMajorCategoryId || undefined,
      subCategoryId: selectedSubCategoryId || undefined,
      package: selectedPackage === "全部封装" ? undefined : selectedPackage,
      owned: selectedOwned === "全部" ? undefined : selectedOwned === "仅持有",
      attributeText: deferredAttributeFilter || undefined,
    });
    setResults(sortSearchResults(searchResults, sortOrder));
  }, [
    deferredSearchText,
    selectedMajorCategoryId,
    selectedSubCategoryId,
    selectedPackage,
    selectedOwned,
    deferredAttributeFilter,
    sortOrder,
    snapshot,
  ]);

  useEffect(() => {
    if (!snackbarVisible) {
      return;
    }

    const timer = window.setTimeout(() => setSnackbarVisible(false), 2600);
    return () => window.clearTimeout(timer);
  }, [snackbarVisible, snackbarMessage]);

  const announceStatus = useCallback((message: string) => {
    setStatusText(message);
    setSnackbarMessage(message);
    setSnackbarVisible(true);
  }, []);

  const applySnapshot = useCallback((nextSnapshot: ExportCatalogSnapshot, message: string) => {
    saveSnapshot(nextSnapshot);
    startTransition(() => setSnapshot(nextSnapshot));
    announceStatus(message);
  }, [announceStatus]);

  const refreshCatalog = useCallback(async () => {
    const refreshGeneration = ++refreshGenerationRef.current;
    setSyncState("discovering");
    setStatusText("正在查找电脑");
    setLastCheckText("正在自动发现 Windows");
    setUpdateStatus("等待目录同步后检查");

    let catalogs;
    try {
      catalogs = await discoverWindowsCatalog();
    } catch (error) {
      if (refreshGeneration !== refreshGenerationRef.current) {
        return;
      }

      setSyncState("error");
      setSyncSource("本地离线快照");
      setLastCheckText(`自动发现失败：${error instanceof Error ? error.message : "未知错误"}`);
      setUpdateStatus("未检查更新；请确认同一 Wi‑Fi 后重新查找电脑。");
      announceStatus(`查找电脑失败: ${error instanceof Error ? error.message : "未知错误"}`);
      return;
    }

    if (refreshGeneration !== refreshGenerationRef.current) {
      return;
    }

    if (catalogs.length === 0) {
      const rememberedCatalog = restoreDiscoveredCatalog(window.localStorage.getItem(LAST_DISCOVERY_URL_KEY));
      if (rememberedCatalog) {
        catalogs = [rememberedCatalog];
        setLastCheckText("未收到自动发现广播，正在尝试上次成功的 Windows 地址");
      }
    }

    if (catalogs.length === 0) {
      setSyncState("offline");
      setSyncSource("本地离线快照");
      setLastCheckText("未发现 Windows；请确认同一 Wi‑Fi 后重新查找电脑。");
      setUpdateStatus("未检查更新；需先连接 Windows。");
      setStatusText("离线目录");
      return;
    }

    const previousServiceUrl = window.localStorage.getItem(LAST_DISCOVERY_URL_KEY);
    const catalog = catalogs.find((item) => item.serviceUrl === previousServiceUrl) ?? catalogs[0];
    setSyncState("syncing");
    setSyncSource("Windows 局域网自动发现");
    setStatusText("正在同步目录");

    try {
      const nextSnapshot = await fetchSnapshotFromPc(catalog);
      if (refreshGeneration !== refreshGenerationRef.current) {
        return;
      }

      window.localStorage.setItem(LAST_DISCOVERY_URL_KEY, catalog.serviceUrl);
      const syncedAt = new Date().toISOString();
      window.localStorage.setItem(LAST_SUCCESS_AT_KEY, syncedAt);
      lastSuccessfulSyncRef.current = Date.now();
      setLastSuccessAt(syncedAt);
      applySnapshot(nextSnapshot, "已同步 · 刚刚");
      setSyncState("synced");
      setLastCheckText("目录同步成功 · 已检查更新");
      setUpdateStatus("正在检查 Windows 发布的应用更新");

      try {
        const update = await fetchAvailableAppUpdate(catalog);
        if (refreshGeneration !== refreshGenerationRef.current) {
          return;
        }

        if (update) {
          const dismissedVersionCode = readDismissedUpdateVersionCode();
          if (dismissedVersionCode === update.manifest.versionCode) {
            setUpdateStatus(`可更新至 v${update.manifest.versionName}（已选择稍后）`);
          } else {
            setUpdateStatus(`可更新至 v${update.manifest.versionName}（code ${update.manifest.versionCode}）`);
            setAvailableAppUpdate(update);
          }
        } else {
          setUpdateStatus("当前已是最新版本，或 Windows 尚未发布更新。");
        }
      } catch (error) {
        if (refreshGeneration === refreshGenerationRef.current) {
          const detail = error instanceof Error ? error.message : "未知错误";
          setUpdateStatus(`更新检查失败：${detail}。可稍后重新查找电脑。`);
          setStatusText("目录已同步；更新检查失败，可稍后重试。");
        }
      }
    } catch (error) {
      if (refreshGeneration !== refreshGenerationRef.current) {
        return;
      }

      setSyncState("error");
      setSyncSource("本地离线快照");
      setLastCheckText(`目录同步失败：${error instanceof Error ? error.message : "未知错误"}`);
      setUpdateStatus("未检查更新；请恢复同步后重试。");
      announceStatus(`同步失败，继续使用离线目录: ${error instanceof Error ? error.message : "未知错误"}`);
    }
  }, [announceStatus, applySnapshot]);

  useEffect(() => {
    LEGACY_SYNC_KEYS.forEach((key) => window.localStorage.removeItem(key));
    void refreshCatalog();

    const refreshWhenReturningToForeground = () => {
      if (
        document.visibilityState === "visible" &&
        Date.now() - lastSuccessfulSyncRef.current > FOREGROUND_REFRESH_INTERVAL_MS
      ) {
        void refreshCatalog();
      }
    };

    document.addEventListener("visibilitychange", refreshWhenReturningToForeground);
    return () => document.removeEventListener("visibilitychange", refreshWhenReturningToForeground);
  }, [refreshCatalog]);

  const installAvailableUpdate = useCallback(async () => {
    if (!availableAppUpdate || isInstallingUpdate) {
      return;
    }

    setIsInstallingUpdate(true);
    try {
      await downloadAndInstallUpdate(availableAppUpdate);
      announceStatus("已交由 Android 系统安装器确认安装。");
    } catch (error) {
      announceStatus(`更新准备失败: ${error instanceof Error ? error.message : "未知错误"}`);
    } finally {
      setIsInstallingUpdate(false);
    }
  }, [announceStatus, availableAppUpdate, isInstallingUpdate]);

  const uniquePackages = Array.from(new Set(snapshot.components.map((component) => component.package).filter(Boolean))).sort();

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
      <div
        className={`android-content-area tab-${activeTab}`}
        data-active-tab={activeTab}
        data-nav-direction={navDirection}
      >
        {activeTab === "catalog" && (
          <CatalogView
            snapshot={snapshot}
            majorCategories={majorCategories}
            results={results}
            selectedMajorCategoryId={selectedMajorCategoryId}
            onSelectMajorCategoryId={(majorCategoryId) => {
              setSelectedMajorCategoryId(majorCategoryId);
              setSelectedSubCategoryId("");
            }}
            selectedSubCategoryId={selectedSubCategoryId}
            onSelectSubCategoryId={setSelectedSubCategoryId}
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
            sortOrder={sortOrder}
            onSelectSortOrder={setSortOrder}
            onOpenSync={() => switchTab("sync")}
          />
        )}

        {activeTab === "sync" && (
          <SyncView
            snapshot={snapshot}
            syncState={syncState}
            statusText={statusText}
            onRetry={() => void refreshCatalog()}
            syncSource={syncSource}
            lastCheckText={lastCheckText}
            lastSuccessAt={lastSuccessAt}
            updateStatus={updateStatus}
            onRetryUpdate={() => void refreshCatalog()}
          />
        )}

        {activeTab === "about" && (
          <AboutView
            snapshot={snapshot}
            syncSource={syncSource}
            lastCheckText={lastCheckText}
            updateStatus={updateStatus}
          />
        )}
      </div>

      <ComponentDetailSheet
        component={selectedComponent}
        majorCategories={majorCategories}
        onClose={() => setSelectedComponent(null)}
      />

      <AppUpdatePrompt
        update={availableAppUpdate}
        isInstalling={isInstallingUpdate}
        onLater={() => {
          const dismissedVersionCode = availableAppUpdate?.manifest.versionCode ?? null;
          if (dismissedVersionCode !== null) {
            window.localStorage.setItem(DISMISSED_UPDATE_VERSION_KEY, String(dismissedVersionCode));
          }
          setAvailableAppUpdate(null);
        }}
        onUpdate={() => void installAvailableUpdate()}
      />

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
