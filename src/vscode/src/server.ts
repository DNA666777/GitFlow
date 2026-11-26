import { ChildProcess, spawn } from 'node:child_process';
import * as fs from 'node:fs';
import * as vscode from 'vscode';
import { createMessageConnection, MessageConnection, StreamMessageReader, StreamMessageWriter } from 'vscode-jsonrpc/node';
import { resolveDotnet } from './dotnet';
import { ServerInfo } from './protocol';

/**
 * The GitFlow.Server process (the shared .NET Core of GitFlow) and its JSON-RPC connection.
 * Started on first use; restarted on the next request if it stopped. All the logic lives in the server.
 */
export class GitFlowServer implements vscode.Disposable {
  private starting: Promise<MessageConnection> | undefined;
  private process: ChildProcess | undefined;
  private connection: MessageConnection | undefined;

  constructor(
    private readonly serverDll: string,
    private readonly output: vscode.LogOutputChannel,
  ) {}

  /** Information returned by "initialize" (server version, language of the messages). */
  info: ServerInfo | undefined;

  /** Sends a request with named parameters. Expected failures come back in the result, not as exceptions. */
  async request<T>(method: string, params: object): Promise<T> {
    const connection = await this.start();
    return connection.sendRequest<T>(method, params);
  }

  start(): Promise<MessageConnection> {
    this.starting ??= this.launch().catch((error: unknown) => {
      this.starting = undefined;
      throw error;
    });
    return this.starting;
  }

  async restart(): Promise<void> {
    this.stop();
    await this.start();
  }

  dispose(): void {
    this.stop();
  }

  private async launch(): Promise<MessageConnection> {
    if (!fs.existsSync(this.serverDll)) {
      throw new Error(vscode.l10n.t('GitFlow server not found: {0}', this.serverDll));
    }

    const dotnet = await resolveDotnet();
    const child = spawn(dotnet, [this.serverDll], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.process = child;

    child.stderr?.setEncoding('utf8');
    child.stderr?.on('data', (text: string) => this.output.warn(text.trimEnd()));
    child.on('error', (error) => this.output.error(`GitFlow.Server: ${error.message}`));
    child.on('exit', (code) => {
      this.output.warn(vscode.l10n.t('GitFlow server stopped (code {0}).', String(code)));
      if (this.process === child) {
        this.reset();
      }
    });

    const connection = createMessageConnection(new StreamMessageReader(child.stdout!), new StreamMessageWriter(child.stdin!));
    // Operations asked by the user at the Info level; reads done to refresh the display at the Trace level
    // (still logged, hidden by default: "Output: Set Log Level" > Trace to see them).
    connection.onNotification('log', (params: { line: string; level?: string }) => {
      const line = params.line.replace(/^\[\d{2}:\d{2}:\d{2}\] /, ''); // the log channel adds its own time
      if (params.level === 'trace') {
        this.output.trace(line);
      } else {
        this.output.info(line);
      }
    });
    connection.onError(([error]) => this.output.error(`GitFlow.Server: ${error.message}`));
    connection.listen();
    this.connection = connection;

    // The language of VS Code decides the language of the messages (fr* → French, otherwise English).
    this.info = await connection.sendRequest<ServerInfo>('initialize', { culture: vscode.env.language });
    this.output.info(`GitFlow.Server ${this.info.version} — ${dotnet} — UI culture: ${this.info.culture || 'invariant'}`);
    return connection;
  }

  private stop(): void {
    const child = this.process;
    this.connection?.dispose();
    this.reset();
    if (child && child.exitCode === null) {
      child.stdin?.end(); // closing stdin ends the server cleanly
      setTimeout(() => {
        if (child.exitCode === null) {
          child.kill();
        }
      }, 3000).unref();
    }
  }

  private reset(): void {
    this.connection = undefined;
    this.process = undefined;
    this.starting = undefined;
  }
}
