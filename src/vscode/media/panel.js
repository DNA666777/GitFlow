// GitFlow panel: renders the model sent by the extension and sends back the clicks. No logic here.
(function () {
  const vscode = acquireVsCodeApi();
  const root = document.getElementById('root');

  // The commit message belongs to the user once edited: a refresh no longer replaces it.
  const saved = vscode.getState() || {};
  let message = saved.message || '';
  let edited = !!saved.edited;
  let startType = saved.startType || 'feature';
  let startName = '';
  let model = null;

  const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  const esc = s => String(s === undefined || s === null ? '' : s).replace(/[&<>"']/g, c => ESCAPES[c]);
  const save = () => vscode.setState({ message, edited, startType });
  const post = (type, data) => vscode.postMessage(Object.assign({ type }, data || {}));

  function render() {
    if (!model) {
      return;
    }

    const L = model.labels;
    let html = '<div class="header"><span class="repo" title="' + esc(model.repositoryName) + '">' + esc(model.repositoryName || 'GitFlow') + '</span>'
      + '<button class="secondary small" data-action="showLog">' + esc(L.showLog) + '</button>'
      + (model.kind === 'ready' ? '<button class="secondary small" data-action="settings">' + esc(L.settings) + '</button>' : '')
      + '</div>';

    if (model.kind === 'noRepository') {
      html += '<p>' + esc(L.noRepository) + '</p>';
    } else if (model.kind === 'notInitialized') {
      html += '<p>' + esc(L.notInitialized) + '</p><button data-action="init">' + esc(L.init) + '</button>';
    } else if (model.kind === 'loading') {
      html += '<p class="muted">' + esc(L.loading) + '</p>';
    } else if (model.kind === 'error') {
      html += '<p>' + esc(L.error) + '</p>';
    } else {
      html += ready(model, L);
    }

    if (model.status) {
      html += '<div class="status-line muted">' + esc(model.status) + '</div>';
    }

    // Keep the focus and the caret of the field being typed in across a refresh.
    const activeId = document.activeElement && document.activeElement.id;
    const caret = activeId ? document.activeElement.selectionStart : null;
    root.innerHTML = html;
    const textarea = document.getElementById('message');
    if (textarea) {
      textarea.value = message;
    }

    const name = document.getElementById('startName');
    if (name) {
      name.value = startName;
    }

    updateCommitButton();
    const active = activeId && document.getElementById(activeId);
    if (active) {
      active.focus();
      if (typeof caret === 'number' && active.setSelectionRange) {
        active.setSelectionRange(caret, caret);
      }
    }
  }

  function ready(m, L) {
    const s = m.state;
    let html = '<div class="grid">'
      + '<span class="muted">' + esc(L.branch) + '</span><span class="value branch" title="' + esc(s.branch) + '">' + esc(s.branch) + '</span>'
      + '<span class="muted">' + esc(L.type) + '</span><span class="value">' + esc(s.type) + '</span>'
      + '<span class="muted">' + esc(L.state) + '</span><span class="value">' + esc(s.workingTree) + '</span>'
      + '<span class="muted">' + esc(L.sync) + '</span><span class="value">' + esc(s.sync) + '</span></div>';

    if (s.releaseReminder) {
      html += '<div class="notice">' + esc(s.releaseReminder) + '</div>';
    }

    if (s.pendingFinish) {
      html += '<div class="notice warning">' + esc(s.pendingFinish.text) + '<br>'
        + '<button data-action="finish" data-branch="' + esc(s.pendingFinish.branch) + '">' + esc(L.resume) + '</button></div>';
    }

    // Commit
    if (m.commit) {
      const c = m.commit;
      html += '<h2>' + esc(L.commitSection) + '</h2><div class="muted">' + esc(c.header) + '</div>';
      if (c.changes.length === 0) {
        html += '<div class="muted italic">' + esc(L.noChanges) + '</div>';
      } else {
        html += '<div class="changes">' + c.changes.map(f =>
          '<div class="change" title="' + esc(f.title) + '"><span class="status ' + esc(f.status) + '">' + esc(f.status) + '</span>'
          + '<span class="ellipsis">' + esc(f.path) + '</span></div>').join('') + '</div>'
          + '<div class="muted italic">' + esc(c.modeText) + '</div>';
      }

      html += '<textarea id="message" placeholder="' + esc(L.commitPlaceholder) + '" aria-label="' + esc(L.commitPlaceholder) + '"></textarea>';
      if (c.blocked) {
        html += '<div class="notice warning">' + esc(c.blocked) + '</div>';
      }

      html += '<div class="actions"><button class="secondary" data-action="regenerate">' + esc(L.regenerate) + '</button>'
        + '<button id="commit" data-action="commit">' + esc(L.commit) + '</button></div>';
    }

    // Pull requests (only when some are followed)
    if (m.pullRequests.length > 0) {
      html += '<h2>' + esc(L.pullRequestsSection) + '</h2><div class="actions"><button class="secondary small" data-action="refreshPullRequests">'
        + esc(L.refreshPullRequests) + '</button></div>';
      for (const g of m.pullRequests) {
        html += '<div class="pr-group"><div class="source">' + esc(g.source) + '</div>'
          + g.items.map(i => '<div class="row"><span class="names">' + esc(i.label) + ' <span class="badge">' + esc(i.state) + '</span></span>'
            + '<button class="secondary small" data-action="openUrl" data-url="' + esc(i.url || '') + '"' + (i.url ? '' : ' disabled') + '>'
            + esc(L.open) + '</button></div>').join('')
          + '<div class="actions">'
          + (g.canTag ? '<button class="small" data-action="tag" data-branch="' + esc(g.source) + '">' + esc(g.tagLabel) + '</button>' : '')
          + (g.canComplete ? '<button class="small" data-action="complete" data-branch="' + esc(g.source) + '">' + esc(L.complete) + '</button>' : '')
          + '</div></div>';
      }
    }

    // Start a branch
    const type = m.start.find(x => x.type === startType) || m.start[0];
    html += '<h2>' + esc(L.startSection) + '</h2><div class="start">'
      + '<select id="startType" aria-label="' + esc(L.type) + '">'
      + m.start.map(x => '<option value="' + esc(x.type) + '"' + (x.type === type.type ? ' selected' : '') + '>' + esc(x.label) + '</option>').join('')
      + '</select>'
      + '<span class="prefix muted">' + esc(type.prefix) + '</span>'
      + '<input id="startName" type="text" placeholder="' + esc(L.namePlaceholder) + '" aria-label="' + esc(L.namePlaceholder) + '">'
      + '<button data-action="start">' + esc(L.start) + '</button></div>';

    // Branches in progress
    html += '<h2>' + esc(L.branchesSection) + '</h2>';
    if (m.groups.every(g => g.branches.length === 0)) {
      html += '<div class="muted">' + esc(L.noBranches) + '</div>';
    }

    // Like Visual Studio, the three groups; when no branch at all is in progress, the sentence above is enough.
    for (const g of m.groups.some(x => x.branches.length > 0) ? m.groups : []) {
      html += '<div class="group muted">' + esc(g.label) + '</div>'
        + g.branches.map(b => '<div class="row"><div class="names"><div class="ellipsis" title="' + esc(b.fullName) + '">' + esc(b.name) + '</div>'
          + (b.badge ? '<div class="badge">' + esc(b.badge) + '</div>' : '') + '</div>'
          + '<button class="secondary small" data-action="publish" data-branch="' + esc(b.fullName) + '">' + esc(L.push) + '</button>'
          + '<button class="small" data-action="finish" data-branch="' + esc(b.fullName) + '">' + esc(L.finish) + '</button></div>').join('');
    }

    return html;
  }

  function currentStart() {
    return model && model.start ? model.start.find(x => x.type === startType) : undefined;
  }

  function updateCommitButton() {
    const button = document.getElementById('commit');
    if (button && model && model.commit) {
      button.disabled = !!model.commit.blocked || model.commit.changes.length === 0 || !message.trim();
    }
  }

  root.addEventListener('click', event => {
    const button = event.target.closest('button[data-action]');
    if (!button || button.disabled) {
      return;
    }

    const action = button.dataset.action;
    if (action === 'regenerate') {
      message = model.commit ? model.commit.suggestedMessage : '';
      edited = false;
      save();
      render();
      post('refresh');
    } else if (action === 'commit') {
      post('commit', { message });
    } else if (action === 'start') {
      post('start', { branchType: startType, name: startName });
    } else if (action === 'openUrl') {
      post('openUrl', { url: button.dataset.url });
    } else {
      post(action, button.dataset.branch ? { branch: button.dataset.branch } : {});
    }
  });

  root.addEventListener('input', event => {
    if (event.target.id === 'message') {
      message = event.target.value;
      edited = true;
      save();
      updateCommitButton();
    } else if (event.target.id === 'startName') {
      startName = event.target.value;
    }
  });

  root.addEventListener('change', event => {
    if (event.target.id === 'startType') {
      startType = event.target.value;
      const type = currentStart();
      startName = type ? type.suggestion : '';
      save();
      render();
    }
  });

  root.addEventListener('keydown', event => {
    if (event.key === 'Enter' && event.target.id === 'startName') {
      post('start', { branchType: startType, name: startName });
    } else if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && event.target.id === 'message') {
      const button = document.getElementById('commit');
      if (button && !button.disabled) {
        post('commit', { message });
      }
    }
  });

  window.addEventListener('message', event => {
    const data = event.data;
    if (data.type === 'model') {
      const first = !model;
      model = data.model;
      if (!edited && model.commit) {
        message = model.commit.suggestedMessage;
      }

      if (first) {
        const type = currentStart();
        startName = type ? type.suggestion : '';
      }

      render();
    } else if (data.type === 'committed') {
      edited = false;
      message = '';
      save();
    } else if (data.type === 'started') {
      const type = currentStart();
      startName = type ? type.suggestion : '';
      render();
    }
  });
})();
