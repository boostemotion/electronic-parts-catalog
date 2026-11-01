import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { SyncView } from "../components/SyncView";

const snapshot = { meta: { exportedAt: "2026-07-12T00:00:00.000Z", componentCount: 0, categories: [], taxonomyVersion: "1", majorCategories: [] }, components: [] };

describe("SyncView", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
  });

  it("contains no manual address, token, or import controls", () => {
    render(<SyncView snapshot={snapshot} syncState="offline" statusText="离线目录" onRetry={vi.fn()} />);
    expect(screen.queryByText("PC 端地址")).not.toBeInTheDocument();
    expect(screen.queryByText(/token/i)).not.toBeInTheDocument();
    expect(screen.queryByText("CSV 导入")).not.toBeInTheDocument();
    expect(screen.queryByText("JSON 导入")).not.toBeInTheDocument();
    expect(screen.getByText("同步来源")).toBeInTheDocument();
    expect(screen.getByText("上次检查")).toBeInTheDocument();
    expect(screen.getByText("应用更新")).toBeInTheDocument();
  });

  it("offers a single automatic rediscovery action", async () => {
    const user = userEvent.setup();
    const retry = vi.fn();

    render(<SyncView snapshot={snapshot} syncState="idle" statusText="待同步" onRetry={retry} />);

    expect(screen.getByRole("heading", { name: "同步" })).toBeInTheDocument();
    expect(screen.queryByText(/移动端只负责查询/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "重新查找电脑" }));
    expect(retry).toHaveBeenCalledTimes(1);
  });
});
