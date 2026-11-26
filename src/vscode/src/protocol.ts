// Messages of GitFlow.Server (see src/GitFlow.Server/Protocol.cs): JSON camelCase, enums as camelCase strings.
// Texts (message, warnings, reminders) are already translated by the server.

export type BranchType = 'feature' | 'release' | 'hotfix';
export type ConfigScope = 'local' | 'global';
export type FlowStatus = 'success' | 'failed' | 'conflict';
export type PullRequestState = 'open' | 'merged' | 'closed';
export type HostingPlatform = 'gitHub' | 'azureDevOps';

export interface ServerInfo {
  version: string;
  culture: string;
}

export interface Branch {
  type: BranchType;
  name: string;
  fullName: string;
}

export interface Naming {
  masterBranch: string;
  developBranch: string;
  featurePrefix: string;
  releasePrefix: string;
  hotfixPrefix: string;
  versionTagPrefix: string;
}

export interface FinishDefaults {
  pushAfterFinish: boolean;
  deleteRemoteBranch: boolean;
  usePullRequest: boolean;
  pruneOnComplete: boolean;
  azureDevOpsServer: boolean;
}

export interface Settings {
  naming: Naming;
  finish: FinishDefaults;
}

export interface RepoState {
  repositoryPath: string;
  isInitialized: boolean;
  naming: Naming | null;
  hasCommits: boolean;
  currentBranch: string | null;
  currentFlowBranch: Branch | null;
  isDirty: boolean;
  hasRemote: boolean;
  isMergeInProgress: boolean;
  pendingFinish: Branch | null;
  ahead: number | null;
  behind: number | null;
  flowBranches: Branch[];
  mergedFlowBranches: string[];
  releaseReminder: string | null;
}

export interface PullRequest {
  id: number;
  source: string;
  target: string;
  state: PullRequestState | null;
  webUrl: string | null;
  title: string | null;
  error: string | null;
}

export interface PullRequestGroup {
  source: string;
  version: string | null;
  pullRequests: PullRequest[];
  canTag: boolean;
  canComplete: boolean;
}

/** Outcome of an operation; `message` is the translated error (empty on success). */
export interface Result {
  success: boolean;
  status: FlowStatus;
  error: string;
  message: string;
  detail: string | null;
  branchName: string | null;
  tagName: string | null;
  warnings: string[];
  conflictedFiles: string[];
  pullRequests: PullRequest[];
}

export interface Remote {
  platform: HostingPlatform;
  webUrl: string;
  onPremises: boolean;
}

export interface FinishContext {
  branch: Branch;
  targets: string[];
  tagName: string | null;
  defaultTagMessage: string;
  isResume: boolean;
  isAlreadyMerged: boolean;
  asksTagMessage: boolean;
  hasRemote: boolean;
  defaults: FinishDefaults;
  remote: Remote | null;
  pullRequestAvailable: boolean;
  pullRequestUnavailableMessage: string;
}

export interface PrepareFinishResponse {
  context: FinishContext | null;
  error: Result | null;
}

export interface PullRequestDraft {
  source: string;
  target: string;
  title: string;
  description: string;
}

export interface PreparePullRequestsResponse {
  drafts: PullRequestDraft[];
  error: Result | null;
}

export type ChangeKind = 'added' | 'modified' | 'deleted' | 'renamed' | 'conflicted';
export type CommitMode = 'stagedOnly' | 'allChanges';

export interface FileChange {
  kind: ChangeKind;
  path: string;
  oldPath: string | null;
}

/** What a commit would contain now, with the message generated from git. */
export interface CommitPreview {
  mode: CommitMode;
  changes: FileChange[];
  suggestedMessage: string;
  isBlocked: boolean;
  blockedMessage: string;
}
