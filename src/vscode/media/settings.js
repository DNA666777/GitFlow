// GitFlow Settings screen: shows the fields, validates while typing (through the extension), sends Save.
(function () {
  const vscode = acquireVsCodeApi();
  const root = document.getElementById('root');

  // Built-in defaults (same as GitFlowConfig.Default and FinishDefaults.None in the Core).
  const DEFAULTS = {
    naming: { masterBranch: 'master', developBranch: 'develop', featurePrefix: 'feature/', releasePrefix: 'release/', hotfixPrefix: 'hotfix/', versionTagPrefix: '' },
    finish: { pushAfterFinish: false, deleteRemoteBranch: false, usePullRequest: false, pruneOnComplete: false, azureDevOpsServer: false },
  };

  let settings = null;
  let scope = 'local';
  let branches = [];
  let initMode = false;
  let L = {};
  let errors = [];
  let timer = null;

  const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  const esc = s => String(s === undefined || s === null ? '' : s).replace(/[&<>"']/g, c => ESCAPES[c]);
  const post = (type, data) => vscode.postMessage(Object.assign({ type }, data || {}));
  const clone = o => JSON.parse(JSON.stringify(o));

  const text = (id, label, value, hint) =>
    '<label class="field"><span>' + esc(label) + '</span><input id="' + id + '" type="text" value="' + esc(value) + '"'
    + (id === 'masterBranch' || id === 'developBranch' ? ' list="branches"' : '') + ' spellcheck="false"></label>'
    + (hint ? '<div class="hint muted">' + esc(hint) + '</div>' : '');

  const check = (id, label, value, hint) =>
    '<label class="check"><input id="' + id + '" type="checkbox"' + (value ? ' checked' : '') + '> <span>' + esc(label) + '</span></label>'
    + (hint ? '<div class="hint muted indent">' + esc(hint) + '</div>' : '');

  const radio = (name, value, label, checked) =>
    '<label class="radio"><input type="radio" name="' + name + '" value="' + value + '"' + (checked ? ' checked' : '') + '> ' + esc(label) + '</label>';

  function render() {
    const n = settings.naming;
    const f = settings.finish;
    let html = '<h1>' + esc(L.title) + '</h1>';
    if (initMode) {
      html += '<p>' + esc(L.initIntro) + '</p>';
    } else {
      html += '<h2>' + esc(L.scope) + '</h2><div class="radios">'
        + radio('scope', 'local', L.scopeLocal, scope === 'local') + radio('scope', 'global', L.scopeGlobal, scope === 'global') + '</div>';
    }

    html += '<datalist id="branches">' + branches.map(b => '<option value="' + esc(b) + '">').join('') + '</datalist>'
      + '<h2>' + esc(L.branches) + '</h2>'
      + text('masterBranch', L.master, n.masterBranch) + text('developBranch', L.develop, n.developBranch)
      + '<h2>' + esc(L.prefixes) + '</h2>'
      + text('featurePrefix', L.feature, n.featurePrefix) + text('releasePrefix', L.release, n.releasePrefix)
      + text('hotfixPrefix', L.hotfix, n.hotfixPrefix) + text('versionTagPrefix', L.tagPrefix, n.versionTagPrefix, L.tagPrefixHint)
      + (initMode ? '' : '<div class="hint muted">' + esc(L.renameNote) + '</div>')
      + '<h2>' + esc(L.finishDefaults) + '</h2>'
      + '<div class="radios"><span>' + esc(L.finishMode) + '</span>'
      + radio('mode', 'local', L.modeLocal, !f.usePullRequest) + radio('mode', 'pullrequest', L.modePullRequest, f.usePullRequest) + '</div>'
      + check('pushAfterFinish', L.push, f.pushAfterFinish)
      + check('deleteRemoteBranch', L.deleteRemote, f.deleteRemoteBranch)
      + check('pruneOnComplete', L.prune, f.pruneOnComplete)
      + check('azureDevOpsServer', L.azureDevOpsServer, f.azureDevOpsServer, L.azureDevOpsServerHint)
      + '<div id="errors"></div>'
      + '<div class="actions spread"><button class="secondary" data-action="reset">' + esc(L.reset) + '</button>'
      + '<span><button class="secondary" data-action="cancel">' + esc(L.cancel) + '</button> '
      + '<button id="save" data-action="save">' + esc(L.save) + '</button></span></div>';
    root.innerHTML = html;
    showErrors();
  }

  function showErrors() {
    const box = document.getElementById('errors');
    if (box) {
      box.innerHTML = errors.length === 0 ? '' : '<div class="notice warning">' + errors.map(esc).join('<br>') + '</div>';
    }

    const save = document.getElementById('save');
    if (save) {
      save.disabled = errors.length > 0;
    }
  }

  function validateLater() {
    clearTimeout(timer);
    timer = setTimeout(() => post('validate', { settings }), 250);
  }

  root.addEventListener('input', event => {
    const id = event.target.id;
    if (id && id in settings.naming) {
      settings.naming[id] = event.target.value;
      validateLater();
    }
  });

  root.addEventListener('change', event => {
    const target = event.target;
    if (target.name === 'scope') {
      scope = target.value;
      post('scope', { scope });
    } else if (target.name === 'mode') {
      settings.finish.usePullRequest = target.value === 'pullrequest';
    } else if (target.type === 'checkbox' && target.id in settings.finish) {
      settings.finish[target.id] = target.checked;
    }
  });

  root.addEventListener('click', event => {
    const button = event.target.closest('button[data-action]');
    if (!button || button.disabled) {
      return;
    }

    if (button.dataset.action === 'reset') {
      settings = clone(DEFAULTS);
      render();
      validateLater();
    } else if (button.dataset.action === 'save') {
      post('save', { settings, scope });
    } else {
      post(button.dataset.action);
    }
  });

  window.addEventListener('message', event => {
    const data = event.data;
    if (data.type === 'load') {
      settings = data.settings;
      scope = data.scope;
      branches = data.branches;
      initMode = data.initMode;
      L = data.labels;
      errors = [];
      render();
      post('validate', { settings });
    } else if (data.type === 'validation') {
      errors = data.errors;
      showErrors();
    }
  });

  post('ready');
})();
