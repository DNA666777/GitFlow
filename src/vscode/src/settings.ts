import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { FlowCommands } from './flow';
import { ConfigScope, RepoState, Settings } from './protocol';
import { GitFlowServer } from './server';

const t = vscode.l10n.t;

/** Messages from the settings screen. */
type SettingsMessage =
  | { type: 'ready' }
  | { type: 'scope'; scope: ConfigScope }
  | { type: 'validate'; settings: Settings }
  | { type: 'save'; settings: Settings; scope: ConfigScope }
  | { type: 'cancel' };

/**
 * The "GitFlow Settings" screen (an editor tab), same fields as the Settings dialog of Visual Studio:
 * scope, permanent branches, prefixes, tag prefix, Finish defaults, live validation, reset to defaults.
 * On a repository that is not initialized yet, it is the "Initialize GitFlow" screen.
 */
export class SettingsEditor implements vscode.Disposable {
  private panel: vscode.WebviewPanel | undefined;
  private repository: string | undefined;
  private validation = 0;

  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly server: GitFlowServer,
    private readonly flow: FlowCommands,
    private readonly findRepository: () => Promise<string | undefined>,
    private readonly onSaved: () => void,
  ) {}

  async open(): Promise<void> {
    const repository = await this.findRepository();
    if (!repository) {
      void vscode.window.showErrorMessage(t('No Git repository found for the open folder or file.'));
      return;
    }

    const state = await this.server.request<RepoState | null>('getState', { repository });
    const initMode = !state?.isInitialized;
    const title = initMode ? t('Initialize GitFlow') : t('GitFlow Settings');
    if (this.panel && this.repository === repository) {
      this.panel.title = title;
      this.panel.reveal();
      return;
    }

    this.panel?.dispose();
    this.repository = repository;
    const media = vscode.Uri.joinPath(this.extensionUri, 'media');
    const panel = vscode.window.createWebviewPanel('gitflow.settings', title, vscode.ViewColumn.Active, {
      enableScripts: true,
      localResourceRoots: [media],
    });
    this.panel = panel;
    panel.webview.html = this.html(panel.webview, media);
    panel.onDidDispose(() => {
      if (this.panel === panel) {
        this.panel = undefined;
      }
    });
    panel.webview.onDidReceiveMessage((message: SettingsMessage) => void this.handle(panel, repository, initMode, message));
  }

  dispose(): void {
    this.panel?.dispose();
  }

  private async handle(panel: vscode.WebviewPanel, repository: string, initMode: boolean, message: SettingsMessage): Promise<void> {
    try {
      switch (message.type) {
        case 'ready':
        case 'scope': {
          const scope = message.type === 'scope' ? message.scope : 'local';
          const [settings, branches] = await Promise.all([
            this.server.request<Settings>('getSettings', { repository, scope }),
            this.server.request<string[]>('getLocalBranches', { repository }),
          ]);
          void panel.webview.postMessage({ type: 'load', settings, scope, branches, initMode, labels: labels(initMode) });
          return;
        }

        case 'validate': {
          // Only the last answer counts: fields are validated while typing.
          const id = ++this.validation;
          const errors = await this.server.request<string[]>('validateNaming', { repository, naming: message.settings.naming });
          if (id === this.validation) {
            void panel.webview.postMessage({ type: 'validation', errors });
          }

          return;
        }

        case 'save': {
          const result = await this.flow.saveSettings(message.settings, initMode ? 'local' : message.scope);
          if (result?.success) {
            this.onSaved();
            panel.dispose();
          }

          return;
        }

        case 'cancel':
          panel.dispose();
          return;
      }
    } catch (error) {
      void vscode.window.showErrorMessage(t('GitFlow: unexpected error. {0}', error instanceof Error ? error.message : String(error)));
    }
  }

  private html(webview: vscode.Webview, media: vscode.Uri): string {
    const nonce = crypto.randomBytes(16).toString('base64');
    const css = webview.asWebviewUri(vscode.Uri.joinPath(media, 'panel.css'));
    const js = webview.asWebviewUri(vscode.Uri.joinPath(media, 'settings.js'));
    return `<!DOCTYPE html>
<html lang="${vscode.env.language}">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="stylesheet" href="${css}">
  <title>GitFlow</title>
</head>
<body class="settings">
  <main id="root"></main>
  <script nonce="${nonce}" src="${js}"></script>
</body>
</html>`;
  }
}

/** Texts of the screen, translated here (the webview has no access to the translations). */
function labels(initMode: boolean): Record<string, string> {
  return {
    title: initMode ? t('Initialize GitFlow') : t('GitFlow Settings'),
    initIntro: t('GitFlow is not initialized in this repository: check the names, then Initialize. The develop branch is created if needed.'),
    scope: t('Scope'),
    scopeLocal: t('This repository'),
    scopeGlobal: t('All my repositories (defaults for new repositories)'),
    branches: t('Permanent branches'),
    master: t('Production'),
    develop: t('Development'),
    prefixes: t('Prefixes'),
    feature: t('Feature'),
    release: t('Release'),
    hotfix: t('Hotfix'),
    tagPrefix: t('Tag prefix'),
    tagPrefixHint: t('Empty by default (tags 1.4.0), or "v" (tags v1.4.0).'),
    renameNote: t('Changing a prefix does not rename existing branches.'),
    finishDefaults: t('Finish defaults'),
    finishMode: t('Default Finish mode'),
    modeLocal: t('Local merge'),
    modePullRequest: t('Pull Request'),
    push: t('Push the branches (and the tag) to origin at the end'),
    deleteRemote: t('Also delete the branch on origin'),
    prune: t('Pull Request: "Complete branch" runs git fetch --prune (removes references to branches deleted on the server)'),
    azureDevOpsServer: t('Pull Request: origin is an Azure DevOps server (company URL)'),
    azureDevOpsServerHint: t('For URLs that are not recognised automatically, such as https://tfs.company.com/tfs/Collection/Project/_git/Repo. Without a git credential, your Windows account is used.'),
    reset: t('Reset to defaults'),
    cancel: t('Cancel'),
    save: initMode ? t('Initialize') : t('Save'),
  };
}
