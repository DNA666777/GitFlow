// Review of the generated pull request messages: one title and one description per target, editable.
(function () {
  const vscode = acquireVsCodeApi();
  const root = document.getElementById('root');
  const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  const esc = s => String(s === undefined || s === null ? '' : s).replace(/[&<>"']/g, c => ESCAPES[c]);
  let drafts = [];

  function render(texts) {
    let html = '<h1>' + esc(texts.title) + '</h1><p class="muted">' + esc(texts.intro) + '</p>';
    drafts.forEach((d, i) => {
      html += '<h2>' + esc(d.source) + ' → ' + esc(d.target) + '</h2>'
        + '<label class="field"><span>' + esc(texts.titleLabel) + '</span><input id="title-' + i + '" type="text"></label>'
        + '<div class="muted">' + esc(texts.descriptionLabel) + '</div>'
        + '<textarea id="description-' + i + '" class="description"></textarea>';
    });
    html += '<div class="actions"><button class="secondary" data-action="cancel">' + esc(texts.cancel) + '</button>'
      + '<button id="create" data-action="create">' + esc(texts.create) + '</button></div>';
    root.innerHTML = html;
    drafts.forEach((d, i) => {
      document.getElementById('title-' + i).value = d.title;
      document.getElementById('description-' + i).value = d.description;
    });
    update();
  }

  // A pull request needs a title.
  function update() {
    document.getElementById('create').disabled = drafts.some(d => !d.title.trim());
  }

  root.addEventListener('input', event => {
    const [kind, index] = event.target.id.split('-');
    if (index !== undefined) {
      drafts[Number(index)][kind] = event.target.value;
      update();
    }
  });

  root.addEventListener('click', event => {
    const button = event.target.closest('button[data-action]');
    if (button && !button.disabled) {
      vscode.postMessage(button.dataset.action === 'create'
        ? { type: 'create', drafts: drafts.map(d => Object.assign({}, d, { title: d.title.trim() })) }
        : { type: 'cancel' });
    }
  });

  window.addEventListener('message', event => {
    if (event.data.type === 'load') {
      drafts = event.data.drafts.map(d => Object.assign({}, d));
      render(event.data.texts);
    }
  });

  vscode.postMessage({ type: 'ready' });
})();
