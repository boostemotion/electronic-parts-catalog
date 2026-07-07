import { useMemo, useRef, useState } from "react";
import type {
  CSSProperties,
  MouseEvent as ReactMouseEvent,
  PointerEvent as ReactPointerEvent,
  TouchEvent as ReactTouchEvent,
} from "react";

const DISMISS_THRESHOLD = 72;
const MAX_DRAG_OFFSET = 220;

interface BottomSheetDragDismissResult {
  dragHandleProps: {
    onPointerDown: (event: ReactPointerEvent<HTMLElement>) => void;
    onPointerMove: (event: ReactPointerEvent<HTMLElement>) => void;
    onPointerUp: (event: ReactPointerEvent<HTMLElement>) => void;
    onPointerCancel: () => void;
    onMouseDown: (event: ReactMouseEvent<HTMLElement>) => void;
    onMouseMove: (event: ReactMouseEvent<HTMLElement>) => void;
    onMouseUp: (event: ReactMouseEvent<HTMLElement>) => void;
    onMouseLeave: (event: ReactMouseEvent<HTMLElement>) => void;
    onTouchStart: (event: ReactTouchEvent<HTMLElement>) => void;
    onTouchMove: (event: ReactTouchEvent<HTMLElement>) => void;
    onTouchEnd: () => void;
    onTouchCancel: () => void;
  };
  isDragging: boolean;
  sheetStyle: CSSProperties | undefined;
  scrimStyle: CSSProperties | undefined;
}

export function useBottomSheetDragDismiss(onDismiss: () => void): BottomSheetDragDismissResult {
  const [dragOffset, setDragOffset] = useState(0);
  const [isDragging, setIsDragging] = useState(false);
  const startYRef = useRef<number | null>(null);

  const resetDrag = () => {
    startYRef.current = null;
    setIsDragging(false);
    setDragOffset(0);
  };

  const beginDrag = (clientY: number) => {
    startYRef.current = clientY;
    setIsDragging(true);
  };

  const updateDrag = (clientY: number) => {
    if (startYRef.current === null) {
      return;
    }

    const offset = Math.max(0, Math.min(MAX_DRAG_OFFSET, clientY - startYRef.current));
    setDragOffset(offset);
  };

  const endDrag = (clientY: number) => {
    if (startYRef.current === null) {
      return;
    }

    const offset = Math.max(0, clientY - startYRef.current);
    if (offset >= DISMISS_THRESHOLD) {
      resetDrag();
      onDismiss();
      return;
    }

    resetDrag();
  };

  const dragHandleProps = {
    onPointerDown: (event: ReactPointerEvent<HTMLElement>) => {
      beginDrag(event.clientY);
    },
    onPointerMove: (event: ReactPointerEvent<HTMLElement>) => {
      updateDrag(event.clientY);
    },
    onPointerUp: (event: ReactPointerEvent<HTMLElement>) => {
      endDrag(event.clientY);
    },
    onPointerCancel: () => {
      resetDrag();
    },
    onMouseDown: (event: ReactMouseEvent<HTMLElement>) => {
      beginDrag(event.clientY);
    },
    onMouseMove: (event: ReactMouseEvent<HTMLElement>) => {
      updateDrag(event.clientY);
    },
    onMouseUp: (event: ReactMouseEvent<HTMLElement>) => {
      endDrag(event.clientY);
    },
    onMouseLeave: (event: ReactMouseEvent<HTMLElement>) => {
      if (isDragging) {
        endDrag(event.clientY);
      }
    },
    onTouchStart: (event: ReactTouchEvent<HTMLElement>) => {
      const touch = event.touches[0];
      if (touch) {
        beginDrag(touch.clientY);
      }
    },
    onTouchMove: (event: ReactTouchEvent<HTMLElement>) => {
      const touch = event.touches[0];
      if (touch) {
        updateDrag(touch.clientY);
      }
    },
    onTouchEnd: () => {
      endDrag(startYRef.current === null ? 0 : startYRef.current + dragOffset);
    },
    onTouchCancel: () => {
      resetDrag();
    },
  };

  const sheetStyle = useMemo(() => {
    if (dragOffset <= 0) {
      return undefined;
    }

    return {
      transform: `translateY(${dragOffset}px)`,
    } satisfies CSSProperties;
  }, [dragOffset]);

  const scrimStyle = useMemo(() => {
    if (dragOffset <= 0) {
      return undefined;
    }

    const opacity = Math.max(0, 1 - dragOffset / 180);
    return {
      opacity,
    } satisfies CSSProperties;
  }, [dragOffset]);

  return {
    dragHandleProps,
    isDragging,
    sheetStyle,
    scrimStyle,
  };
}
