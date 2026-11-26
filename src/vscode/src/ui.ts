import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { PullRequestDraft } from './protocol';

/** A choice of a list (e.g. the Finish options, the Finish mode). */
export interface Option {
  id: string;
  label: string;
  picked: boolean;
  detail?: string;
}

/** Texts of the review of the generated pull request messages. */
export interface PullRequestReview {
  title: string;
  intro: string;
  titleLabel: string;
  descriptionLabel: string;
  create: string;
  cancel: string;
}

/**
 * Everything the commands ask or show. The VS Code implementation below is the real one;
 * the extension tests replace it with scripted answers (no dialog can be clicked in a test).
 * `undefined` always means "cancelled by the user".
 */
export interface Ui {
  input(prompt: string, value: string, validate?: (value: string) => string | undefined): Promise<string | undefined>;
  confirm(message: string, detail: string, action: string): Promise<boolean>;
  pickOne(title: string, options: Option[]): Promise<string | undefined>;
  pickMany(title: string, options: Option[]): Promise<string[] | undefined>;
  reviewPullRequests(texts: PullRequestReview, drafts: PullRequestDraft[]): Promise<PullRequestDraft[] | undefined>;
  info(message: string, detail?: string): Promise<void>;
  error(message: string, detail?: string): Promise<void>;
  progress<T>(title: string, task: () => Promise<T>): Promise<T>;
}

export class VsCodeUi implements Ui {
  /** @param extensionUri Root of the extension (media of the review screen). */
  constructor(private readonly extensionUri?: vscode.Uri) {}

  async input(prompt: string, value: string, validate?: (value: string) => string | undefined): Promise<string | undefined> {
    const answer = await vscode.window.showInputBox({ title: 'GitFlow', prompt, value, ignoreFocusOut: true, validateInput: validate });
    return answer?.trim();
  }

  async confirm(message: string, detail: string, action: string): Promise<boolean> {
    return (await vscode.window.showInformationMessage(message, { modal: true, detail }, action)) === action;
  }

  async pickOne(title: string, options: Option[]): Promise<string | undefined> {
    // The option picked by default comes first, so that Enter keeps it.
    const ordered = [...options].sort((a, b) => Number(b.picked) - Number(a.picked));
    const items = ordered.map(o => ({ label: o.label, detail: o.detail, id: o.id }));
    return (await vscode.window.showQuickPick(items, { title, ignoreFocusOut: true }))?.id;
  }

  async pickMany(title: string, options: Option[]): Promise<string[] | undefined> {
    const items = options.map(o => ({ label: o.label, picked: o.picked, id: o.id, detail: o.detail }));
    const picked = await vscode.window.showQuickPick(items, { title, canPickMany: true, ignoreFocusOut: true });
    return picked?.map(p => p.id);
  }

  /** An editor tab with the title and description of each pull request, editable, then Create or Cancel. */
  reviewPullRequests(texts: PullRequestReview, drafts: PullRequestDraft[]): Promise<PullRequestDraft[] | undefined> {
    const media = vscode.Uri.joinPath(this.extensionUri ?? vscode.Uri.file('.'), 'media');
    const panel = vscode.window.createWebviewPanel('gitflow.pullRequests', texts.title, vscode.ViewColumn.Active, {
      enableScripts: true,
      localResourceRoots: [media],
    });
    const nonce = crypto.randomBytes(16).toString('base64');
    const css = panel.webview.asWebviewUri(vscode.Uri.joinPath(media, 'panel.css'));
    const js = panel.webview.asWebviewUri(vscode.Uri.joinPath(media, 'pullrequests.js'));
    panel.webview.html = `<!DOCTYPE html>
<html lang="${vscode.env.language}">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${panel.webview.cspSource}; script-src 'nonce-${nonce}';">
  <link rel="stylesheet" href="${css}">
</head>
<body class="settings">
  <main id="root"></main>
  <script nonce="${nonce}" src="${js}"></script>
</body>
</html>`;

    return new Promise(resolve => {
      let answered = false;
      panel.webview.onDidReceiveMessage((message: { type: 'ready' | 'create' | 'cancel'; drafts?: PullRequestDraft[] }) => {
        if (message.type === 'ready') {
          void panel.webview.postMessage({ type: 'load', texts, drafts });
          return;
        }

        answered = true;
        resolve(message.type === 'create' ? message.drafts : undefined);
        panel.dispose();
      });
      panel.onDidDispose(() => {
        if (!answered) {
          resolve(undefined);
        }
      });
    });
  }

  // Modal only when there is something to read (warnings, list of files); otherwise a notification.
  // A notification's promise only settles when the user closes it: never await it, or the command
  // (and the "one operation at a time" lock) would stay pending while it sits in the notification center.

  async info(message: string, detail?: string): Promise<void> {
    if (detail) {
      await vscode.window.showInformationMessage(message, { modal: true, detail });
    } else {
      void vscode.window.showInformationMessage(message);
    }
  }

  async error(message: string, detail?: string): Promise<void> {
    if (detail) {
      await vscode.window.showErrorMessage(message, { modal: true, detail });
    } else {
      void vscode.window.showErrorMessage(message);
    }
  }

  progress<T>(title: string, task: () => Promise<T>): Promise<T> {
    return Promise.resolve(vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title }, task));
  }
}
