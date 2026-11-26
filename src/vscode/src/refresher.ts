import * as vscode from 'vscode';

/** Quiet time before an automatic refresh. */
export const REFRESH_DELAY_MS = 1000;

/**
 * When to refresh the GitFlow panel: now (Refresh button), or once after a quiet second to coalesce bursts of
 * events (an operation, the git extension, saves, the window getting the focus back). Nothing while the panel
 * is hidden: one refresh when it shows again.
 */
export class Refresher implements vscode.Disposable {
  private timer: NodeJS.Timeout | undefined;
  private visible = true;
  private stale = false;

  constructor(private readonly refreshNow: () => void) {}

  attach(view: { visible: boolean; onDidChangeVisibility: vscode.Event<unknown> }): vscode.Disposable {
    this.visible = view.visible;
    return view.onDidChangeVisibility(() => {
      this.visible = view.visible;
      if (this.visible && this.stale) {
        this.refresh();
      }
    });
  }

  refresh(debounce = false): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }

    if (!this.visible) {
      this.stale = true;
      return;
    }

    const fire = () => {
      this.timer = undefined;
      this.stale = false;
      this.refreshNow();
    };
    if (debounce) {
      this.timer = setTimeout(fire, REFRESH_DELAY_MS);
    } else {
      fire();
    }
  }

  dispose(): void {
    if (this.timer) {
      clearTimeout(this.timer);
    }
  }
}
