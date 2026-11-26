import * as vscode from 'vscode';

/** A choice of a multi-select list (e.g. the Finish options). */
export interface Option {
  id: string;
  label: string;
  picked: boolean;
}

/**
 * Everything the commands ask or show. The VS Code implementation below is the real one;
 * the extension tests replace it with scripted answers (no dialog can be clicked in a test).
 * `undefined` always means "cancelled by the user".
 */
export interface Ui {
  input(prompt: string, value: string, validate?: (value: string) => string | undefined): Promise<string | undefined>;
  confirm(message: string, detail: string, action: string): Promise<boolean>;
  pickMany(title: string, options: Option[]): Promise<string[] | undefined>;
  info(message: string, detail?: string): Promise<void>;
  error(message: string, detail?: string): Promise<void>;
  progress<T>(title: string, task: () => Promise<T>): Promise<T>;
}

export class VsCodeUi implements Ui {
  async input(prompt: string, value: string, validate?: (value: string) => string | undefined): Promise<string | undefined> {
    const answer = await vscode.window.showInputBox({ title: 'GitFlow', prompt, value, ignoreFocusOut: true, validateInput: validate });
    return answer?.trim();
  }

  async confirm(message: string, detail: string, action: string): Promise<boolean> {
    return (await vscode.window.showInformationMessage(message, { modal: true, detail }, action)) === action;
  }

  async pickMany(title: string, options: Option[]): Promise<string[] | undefined> {
    const items = options.map(o => ({ label: o.label, picked: o.picked, id: o.id }));
    const picked = await vscode.window.showQuickPick(items, { title, canPickMany: true, ignoreFocusOut: true });
    return picked?.map(p => p.id);
  }

  async info(message: string, detail?: string): Promise<void> {
    // Modal only when there is something to read (warnings, list of files); otherwise a notification.
    if (detail) {
      await vscode.window.showInformationMessage(message, { modal: true, detail });
    } else {
      void vscode.window.showInformationMessage(message);
    }
  }

  async error(message: string, detail?: string): Promise<void> {
    await vscode.window.showErrorMessage(message, { modal: !!detail, detail });
  }

  progress<T>(title: string, task: () => Promise<T>): Promise<T> {
    return Promise.resolve(vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title }, task));
  }
}
