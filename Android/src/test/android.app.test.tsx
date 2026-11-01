import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";

import App from "../App";
import type { ExportCatalogSnapshot } from "@shared/types";

const snapshot: ExportCatalogSnapshot = {
  meta: {
    exportedAt: "2026-07-04T00:00:00.000Z",
    componentCount: 2,
    categories: ["电源", "电阻"],
  },
  components: [
    {
      id: "ldo-1",
      name: "3.3V 稳压器",
      partNumber: "AMS1117-3.3",
      category: "电源",
      package: "SOT223",
      aliases: ["LDO 3v3"],
      keywords: ["ldo", "3.3v"],
      attributes: { voltage: "3.3V" },
      owned: true,
      location: "抽屉 A1",
      substitutes: [],
      notes: "常用稳压器",
      sourceType: "manual",
    },
    {
      id: "res-1",
      name: "10k 电阻",
      partNumber: "RES-10K-0603-1%",
      category: "电阻",
      package: "0603",
      aliases: [],
      keywords: ["10k", "0603"],
      attributes: { resistance: "10k", tolerance: "1%" },
      owned: true,
      location: "电阻盒",
      substitutes: [],
      notes: "",
      sourceType: "manual",
    },
  ],
};

describe("Android catalog app", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("shows a compact catalog dashboard without verbose intro copy", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));

    render(<App />);

    await screen.findByText("随手查料");
    expect(screen.getByText("目录总数")).toBeInTheDocument();
    expect(screen.getByText("大类数")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "筛选与排序" })).toBeInTheDocument();
    expect(screen.queryByText("移动查询端")).not.toBeInTheDocument();
    expect(screen.queryByText(/沿用 WinUI 3 的信息层次/)).not.toBeInTheDocument();
  });

  it("shows an honest empty-state flow instead of demo components on first launch", async () => {
    const user = userEvent.setup();

    render(<App />);

    expect(await screen.findByText("还没有元件目录")).toBeInTheDocument();
    expect(screen.getByText("请确认 Windows 已开启手机自动同步，然后重新查找目录。")).toBeInTheDocument();
    expect(screen.queryByText("3.3V 稳压器")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "前往同步" }));

    expect(await screen.findByText("同步")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "数据同步" })).toHaveAttribute("aria-current", "page");
  });

  it("uses clean bottom navigation labels and marks the active destination", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    const catalogButton = screen.getByRole("button", { name: "元件目录" });
    const syncButton = screen.getByRole("button", { name: "数据同步" });
    const aboutButton = screen.getByRole("button", { name: "关于" });

    expect(catalogButton).toHaveAttribute("aria-current", "page");
    expect(syncButton).not.toHaveAttribute("aria-current");
    expect(aboutButton).not.toHaveAttribute("aria-current");

    await user.click(syncButton);

    expect(await screen.findByText("同步")).toBeInTheDocument();
    expect(syncButton).toHaveAttribute("aria-current", "page");
    expect(catalogButton).not.toHaveAttribute("aria-current");
  });

  it("tracks forward and backward tab motion for animated page transitions", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    const contentArea = document.querySelector(".android-content-area");
    expect(contentArea).toHaveAttribute("data-nav-direction", "none");

    await user.click(screen.getByRole("button", { name: "关于" }));
    expect(await screen.findByRole("heading", { name: "关于" })).toBeInTheDocument();
    expect(contentArea).toHaveAttribute("data-nav-direction", "forward");

    await user.click(screen.getByRole("button", { name: "元件目录" }));
    expect(await screen.findByText("随手查料")).toBeInTheDocument();
    expect(contentArea).toHaveAttribute("data-nav-direction", "backward");
  });

  it("separates the unified app version from the taxonomy protocol version", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);
    await user.click(screen.getByRole("button", { name: "关于" }));

    expect(await screen.findByText("v1.1 (code 4)")).toBeInTheDocument();
    expect(screen.getByText("目录分类版本")).toBeInTheDocument();
    expect(screen.getByText("v1")).toBeInTheDocument();
  });

  it("clears the active field focus when switching tabs", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    const searchInput = await screen.findByPlaceholderText("搜索型号、封装、参数...");
    searchInput.focus();
    expect(document.activeElement).toBe(searchInput);

    await user.click(screen.getByRole("button", { name: "数据同步" }));

    expect(await screen.findByText("同步")).toBeInTheDocument();
    expect(document.activeElement).not.toBe(searchInput);
  });

  it("opens a dedicated filter sheet from the catalog home", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.click(screen.getByRole("button", { name: "筛选与排序" }));

    expect(await screen.findByText("筛选与排序")).toBeInTheDocument();
    expect(screen.getByText("封装")).toBeInTheDocument();
    expect(screen.getByText("持有状态")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("例如 voltage=3.3V")).toBeInTheDocument();
  });

  it("dismisses the filter sheet with a downward drag on the handle", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.click(screen.getByRole("button", { name: "筛选与排序" }));
    expect(await screen.findByText("筛选与排序")).toBeInTheDocument();

    const handle = document.querySelector(".md3-filter-sheet .md3-bottom-sheet-drag-handle");
    expect(handle).not.toBeNull();

    fireEvent.mouseDown(handle!, { clientY: 12 });
    fireEvent.mouseMove(handle!, { clientY: 108 });
    fireEvent.mouseUp(handle!, { clientY: 108 });

    await waitFor(() => {
      expect(screen.queryByText("持有状态")).not.toBeInTheDocument();
    });
  });

  it("filters results through the Material 3 taxonomy sheet", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.click(screen.getByRole("button", { name: "筛选与排序" }));
    expect(await screen.findByText("大类")).toBeInTheDocument();
    expect(screen.getByText("持有状态")).toBeInTheDocument();
  });

  it("supports searching and shows component details", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.type(screen.getByPlaceholderText("搜索型号、封装、参数..."), "AMS1117");

    await waitFor(() => {
      expect(screen.getAllByText("3.3V 稳压器").length).toBeGreaterThan(0);
      expect(screen.queryByText("10k 电阻")).not.toBeInTheDocument();
    });

    expect(screen.getByText(/命中 /)).toBeInTheDocument();
    expect(screen.queryByText(/命中 (partNumber|keywords|category|attributes|aliases|package|name)/)).not.toBeInTheDocument();

    expect(screen.queryByText("常用稳压器")).not.toBeInTheDocument();

    await user.click(screen.getAllByText("3.3V 稳压器")[0]);
    expect((await screen.findAllByText("AMS1117-3.3")).length).toBeGreaterThan(0);
    expect(screen.getAllByText("SOT223").length).toBeGreaterThan(0);
    expect(screen.getByText("技术参数")).toBeInTheDocument();
    expect(screen.getByText("voltage")).toBeInTheDocument();
    expect(screen.getByText("3.3V")).toBeInTheDocument();
    expect(screen.getByText("常用稳压器")).toBeInTheDocument();
  });

  it("dismisses the component detail sheet with the same downward drag gesture", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.click(screen.getAllByText("3.3V 稳压器")[0]);
    expect(await screen.findByText("技术参数")).toBeInTheDocument();

    const handle = document.querySelector(".md3-bottom-sheet.open .md3-bottom-sheet-drag-handle");
    expect(handle).not.toBeNull();

    fireEvent.mouseDown(handle!, { clientY: 24 });
    fireEvent.mouseMove(handle!, { clientY: 126 });
    fireEvent.mouseUp(handle!, { clientY: 126 });

    await waitFor(() => {
      expect(screen.queryByText("技术参数")).not.toBeInTheDocument();
    });
  });

  it("keeps component details read-only", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await screen.findByText("随手查料");
    await user.click(screen.getAllByText("3.3V 稳压器")[0]);
    expect(await screen.findByText("技术参数")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "删除元件" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "修改属性" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "新建元件" })).not.toBeInTheDocument();
  });

  it("shows a read-only automatic discovery workspace without address or import controls", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await user.click(screen.getByRole("button", { name: /数据同步/ }));

    expect(await screen.findByText("同步")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Windows 自动同步" })).toBeInTheDocument();
    expect(screen.queryByText("PC 端地址")).not.toBeInTheDocument();
    expect(screen.queryByText("CSV 导入")).not.toBeInTheDocument();
    expect(screen.queryByText("JSON 导入")).not.toBeInTheDocument();
    expect(screen.queryByText(/保持同一份目录/)).not.toBeInTheDocument();
    expect(screen.queryByText("同步方式")).not.toBeInTheDocument();
  });

  it("shows a compact about page centered on snapshot status", async () => {
    window.localStorage.setItem("catalog.snapshot", JSON.stringify(snapshot));
    const user = userEvent.setup();

    render(<App />);

    await user.click(screen.getByRole("button", { name: /关于/ }));

    expect(await screen.findByRole("heading", { name: "关于" })).toBeInTheDocument();
    expect(screen.getByText("数据状态")).toBeInTheDocument();
    expect(screen.getByText("快照时间")).toBeInTheDocument();
    expect(screen.getByText("电子元件入库台 Android 查询端")).toBeInTheDocument();
    expect(screen.queryByText(/v2\.1\.0/)).not.toBeInTheDocument();
    expect(screen.queryByText("查询端定位")).not.toBeInTheDocument();
    expect(screen.queryByText("与 Windows 协同")).not.toBeInTheDocument();
  });
});
