import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { SyncView } from "../components/SyncView";

const applySnapshot = vi.fn();
const importCsv = vi.fn();

describe("SyncView", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    applySnapshot.mockReset();
    importCsv.mockReset();
  });

  it("normalizes the PC address before syncing", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith("/components")) {
          return new Response(JSON.stringify({ results: [] }), { status: 200 });
        }
        return new Response(JSON.stringify({ categories: ["全部"] }), { status: 200 });
      }),
    );

    render(
      <SyncView statusText="待同步" onApplySnapshot={applySnapshot} onImportCsv={importCsv} />,
    );

    const input = screen.getByPlaceholderText("http://192.168.1.100:10849");
    await user.clear(input);
    await user.type(input, "192.168.1.8:10849/");
    await user.click(screen.getByRole("button", { name: "立即连接并同步" }));

    await waitFor(() => {
      expect(applySnapshot).toHaveBeenCalledTimes(1);
      expect(window.localStorage.getItem("sync_pc_address")).toBe("http://192.168.1.8:10849");
    });
  });

  it("shows inline guidance instead of a browser alert when CSV content is empty", async () => {
    const user = userEvent.setup();

    render(
      <SyncView statusText="待同步" onApplySnapshot={applySnapshot} onImportCsv={importCsv} />,
    );

    await user.click(screen.getByRole("button", { name: "跳过重复" }));

    expect(await screen.findByText("请先选择 CSV 文件或粘贴 CSV 文本。")).toBeInTheDocument();
    expect(importCsv).not.toHaveBeenCalled();
  });

  it("renders sync progress as a structured activity log instead of raw text blocks", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.endsWith("/components")) {
          return new Response(JSON.stringify({ results: [] }), { status: 200 });
        }
        return new Response(JSON.stringify({ categories: ["全部"] }), { status: 200 });
      }),
    );

    render(
      <SyncView statusText="待同步" onApplySnapshot={applySnapshot} onImportCsv={importCsv} />,
    );

    expect(screen.getByRole("heading", { name: "同步" })).toBeInTheDocument();
    expect(screen.queryByText(/移动端只负责查询/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "立即连接并同步" }));

    expect(await screen.findByText("记录")).toBeInTheDocument();
    expect(screen.getByText("正在尝试连接局域网 PC 端 API 服务...")).toBeInTheDocument();
    expect(screen.getByText("正在拉取元件数据库和分类列表...")).toBeInTheDocument();
    expect(screen.getByText(/局域网同步成功/)).toBeInTheDocument();
  });
});
