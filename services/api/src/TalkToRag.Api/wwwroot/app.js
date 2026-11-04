// talk-to-rag Web UI Application
(() => {
  'use strict';

  // --- State ---
  const state = {
    token: localStorage.getItem('talk_to_rag_token') || null,
    user: JSON.parse(localStorage.getItem('talk_to_rag_user') || 'null'),
    settings: JSON.parse(localStorage.getItem('talk_to_rag_settings') || JSON.stringify({
      mode: 'hybrid',
      limit: 20,
      expand: false,
      cutoff: 0.7,
    })),
    sources: [],
    notebooks: [],
    selectedNotebookId: 'all',
    currentConversationId: null,
    currentResults: null,
    pollTimer: null,
    isRecording: false,
    speechRecognition: null,
    isSpeaking: false,
  };

  // --- API Helper ---
  async function apiFetch(endpoint, options = {}) {
    const headers = options.headers || {};
    if (state.token) {
      headers['Authorization'] = `Bearer ${state.token}`;
    }
    if (!(options.body instanceof FormData) && !headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }

    const response = await fetch(endpoint, {
      ...options,
      headers,
    });

    if (response.status === 401) {
      signOut();
      showAuthModal(true);
      throw new Error('Session expired or unauthorized. Please sign in.');
    }

    if (!response.ok) {
      let errorMsg = `Error ${response.status}: ${response.statusText}`;
      try {
        const errorJson = await response.json();
        errorMsg = errorJson.detail || errorJson.title || errorJson.message || JSON.stringify(errorJson);
      } catch {
        const text = await response.text();
        if (text) errorMsg = text;
      }
      throw new Error(errorMsg);
    }

    if (response.status === 204) return null;
    return await response.json();
  }

  // --- Source File URL Helper ---
  function getSourceFileUrl(sourceId, pageNumber = null, snippet = null) {
    let url = `/api/sources/${sourceId}/file?token=${encodeURIComponent(state.token || '')}`;
    if (pageNumber) {
      url += `#page=${pageNumber}`;
    } else if (snippet) {
      const clean = snippet.replace(/<[^>]*>/g, '').trim().slice(0, 80);
      if (clean) {
        url += `#:~:text=${encodeURIComponent(clean)}`;
      }
    }
    return url;
  }

  // --- Native Dialog Light Dismiss Fallback ---
  function setupDialogLightDismiss(dialogEl) {
    if (!dialogEl) return;
    if (!('closedBy' in HTMLDialogElement.prototype)) {
      dialogEl.addEventListener('click', (event) => {
        if (event.target !== dialogEl) return;
        const rect = dialogEl.getBoundingClientRect();
        const isInside = (
          rect.top <= event.clientY &&
          event.clientY <= rect.top + rect.height &&
          rect.left <= event.clientX &&
          event.clientX <= rect.left + rect.width
        );
        if (!isInside) dialogEl.close();
      });
    }
  }

  function openModal(dialogEl) {
    if (dialogEl && typeof dialogEl.showModal === 'function') {
      dialogEl.showModal();
    }
  }

  function closeModal(dialogEl) {
    if (dialogEl && typeof dialogEl.close === 'function') {
      dialogEl.close();
    }
  }

  // --- DOM Elements ---
  const el = {
    // Navigation
    tabs: document.querySelectorAll('.view-toggles .nav-btn'),
    panes: document.querySelectorAll('.view-pane'),
    openAuthBtn: document.getElementById('openAuthBtn'),
    userBadge: document.getElementById('userBadge'),
    userEmailDisplay: document.getElementById('userEmailDisplay'),
    signOutBtn: document.getElementById('signOutBtn'),
    openSettingsBtn: document.getElementById('openSettingsBtn'),

    // Modals
    authModal: document.getElementById('authModal'),
    authModalTitle: document.getElementById('authModalTitle'),
    closeAuthModalBtn: document.getElementById('closeAuthModalBtn'),
    authEmail: document.getElementById('authEmail'),
    authDisplayName: document.getElementById('authDisplayName'),
    displayNameGroup: document.getElementById('displayNameGroup'),
    authPassword: document.getElementById('authPassword'),
    authSubmitBtn: document.getElementById('authSubmitBtn'),
    authErrorMsg: document.getElementById('authErrorMsg'),
    authToggleText: document.getElementById('authToggleText'),
    authToggleLink: document.getElementById('authToggleLink'),

    notebookModal: document.getElementById('notebookModal'),
    closeNotebookModalBtn: document.getElementById('closeNotebookModalBtn'),
    notebookNameInput: document.getElementById('notebookNameInput'),
    notebookDescInput: document.getElementById('notebookDescInput'),
    notebookErrorMsg: document.getElementById('notebookErrorMsg'),
    createNotebookSubmitBtn: document.getElementById('createNotebookSubmitBtn'),

    settingsModal: document.getElementById('settingsModal'),
    closeSettingsModalBtn: document.getElementById('closeSettingsModalBtn'),
    settingsModeSelect: document.getElementById('settingsModeSelect'),
    settingsLimitInput: document.getElementById('settingsLimitInput'),
    settingsExpandCheckbox: document.getElementById('settingsExpandCheckbox'),
    settingsCutoffInput: document.getElementById('settingsCutoffInput'),
    saveSettingsBtn: document.getElementById('saveSettingsBtn'),
    settingsSummaryPill: document.getElementById('settingsSummaryPill'),

    // Sources View
    refreshSourcesBtn: document.getElementById('refreshSourcesBtn'),
    notebookPills: document.getElementById('notebookPills'),
    newNotebookBtn: document.getElementById('newNotebookBtn'),
    dropzone: document.getElementById('dropzone'),
    fileInput: document.getElementById('fileInput'),
    browseFilesBtn: document.getElementById('browseFilesBtn'),
    uploadProgress: document.getElementById('uploadProgress'),
    uploadStatusText: document.getElementById('uploadStatusText'),
    sourcesGrid: document.getElementById('sourcesGrid'),

    // Search View
    searchInput: document.getElementById('searchInput'),
    voiceSearchBtn: document.getElementById('voiceSearchBtn'),
    searchSubmitBtn: document.getElementById('searchSubmitBtn'),
    searchNotebookSelect: document.getElementById('searchNotebookSelect'),
    summarySection: document.getElementById('summarySection'),
    summaryAnswer: document.getElementById('summaryAnswer'),
    summaryCitationsList: document.getElementById('summaryCitationsList'),
    speakSummaryBtn: document.getElementById('speakSummaryBtn'),
    resultsContainer: document.getElementById('resultsContainer'),
    resultsHeader: document.getElementById('resultsHeader'),
    resultsQueryTitle: document.getElementById('resultsQueryTitle'),
    generateSummaryBtn: document.getElementById('generateSummaryBtn'),
    passagesGroups: document.getElementById('passagesGroups'),

    // History View
    refreshHistoryBtn: document.getElementById('refreshHistoryBtn'),
    historyList: document.getElementById('historyList'),
    historyDetails: document.getElementById('historyDetails'),
  };

  let authMode = 'login'; // 'login' or 'register'

  // --- Auth Functions ---
  function updateAuthUI() {
    if (state.token && state.user) {
      el.openAuthBtn.style.display = 'none';
      el.userBadge.style.display = 'inline-flex';
      el.userEmailDisplay.textContent = state.user.displayName || state.user.email;
    } else {
      el.openAuthBtn.style.display = 'inline-flex';
      el.userBadge.style.display = 'none';
      el.userEmailDisplay.textContent = '';
    }
  }

  function showAuthModal(show, mode = 'login') {
    authMode = mode;
    el.authErrorMsg.style.display = 'none';
    el.authErrorMsg.textContent = '';
    if (authMode === 'login') {
      el.authModalTitle.textContent = 'Sign In to talk-to-rag';
      el.displayNameGroup.style.display = 'none';
      el.authSubmitBtn.textContent = 'Sign In';
      el.authToggleText.textContent = 'Need an account?';
      el.authToggleLink.textContent = 'Register';
    } else {
      el.authModalTitle.textContent = 'Create an Account';
      el.displayNameGroup.style.display = 'block';
      el.authSubmitBtn.textContent = 'Register';
      el.authToggleText.textContent = 'Already have an account?';
      el.authToggleLink.textContent = 'Sign In';
    }
    if (show) {
      openModal(el.authModal);
      el.authEmail.focus();
    } else {
      closeModal(el.authModal);
    }
  }

  async function handleAuthSubmit() {
    el.authErrorMsg.style.display = 'none';
    const email = el.authEmail.value.trim();
    const password = el.authPassword.value;
    const displayName = el.authDisplayName.value.trim();

    if (!email || !password) {
      el.authErrorMsg.textContent = 'Please fill in email and password.';
      el.authErrorMsg.style.display = 'block';
      return;
    }

    try {
      let data;
      if (authMode === 'login') {
        data = await apiFetch('/api/auth/login', {
          method: 'POST',
          body: JSON.stringify({ email, password }),
        });
      } else {
        data = await apiFetch('/api/auth/register', {
          method: 'POST',
          body: JSON.stringify({ email, password, displayName: displayName || email.split('@')[0] }),
        });
      }

      state.token = data.accessToken;
      state.user = data.user;
      localStorage.setItem('talk_to_rag_token', state.token);
      localStorage.setItem('talk_to_rag_user', JSON.stringify(state.user));

      updateAuthUI();
      showAuthModal(false);
      loadAllData();
    } catch (err) {
      el.authErrorMsg.textContent = err.message;
      el.authErrorMsg.style.display = 'block';
    }
  }

  function signOut() {
    state.token = null;
    state.user = null;
    localStorage.removeItem('talk_to_rag_token');
    localStorage.removeItem('talk_to_rag_user');
    updateAuthUI();
    stopPolling();
    renderSources([]);
    renderNotebookPills([]);
  }

  // --- Tab Navigation ---
  function switchTab(viewName) {
    el.tabs.forEach(tab => {
      const active = tab.dataset.view === viewName;
      tab.classList.toggle('active', active);
      tab.setAttribute('aria-selected', active);
    });
    el.panes.forEach(pane => {
      pane.classList.toggle('active', pane.id === `view-${viewName}`);
    });

    if (viewName === 'sources') loadSources();
    if (viewName === 'history') loadHistory();
  }

  // --- Notebooks ---
  async function loadNotebooks() {
    if (!state.token) return;
    try {
      const notebooks = await apiFetch('/api/notebooks');
      state.notebooks = notebooks;
      renderNotebookPills(notebooks);
      renderNotebookScopeSelect(notebooks);
    } catch (err) {
      console.error('Failed to load notebooks:', err);
    }
  }

  function renderNotebookPills(notebooks) {
    const container = el.notebookPills;
    container.innerHTML = '';

    const allBtn = document.createElement('button');
    allBtn.className = `pill-btn ${state.selectedNotebookId === 'all' ? 'active' : ''}`;
    allBtn.dataset.notebook = 'all';
    allBtn.textContent = 'All Sources';
    allBtn.addEventListener('click', () => {
      state.selectedNotebookId = 'all';
      renderNotebookPills(state.notebooks);
      renderSources(state.sources);
    });
    container.appendChild(allBtn);

    notebooks.forEach(nb => {
      const btn = document.createElement('button');
      btn.className = `pill-btn ${state.selectedNotebookId === nb.id ? 'active' : ''}`;
      btn.dataset.notebook = nb.id;
      btn.innerHTML = `${escapeHtml(nb.name)} <span class="pill-count">${nb.sourceCount}</span>`;
      btn.addEventListener('click', () => {
        state.selectedNotebookId = nb.id;
        renderNotebookPills(state.notebooks);
        renderSources(state.sources);
      });
      container.appendChild(btn);
    });
  }

  function renderNotebookScopeSelect(notebooks) {
    const select = el.searchNotebookSelect;
    const currentVal = select.value;
    select.innerHTML = '<option value="">All Ready Sources</option>';
    notebooks.forEach(nb => {
      const opt = document.createElement('option');
      opt.value = nb.id;
      opt.textContent = `Notebook: ${nb.name}`;
      select.appendChild(opt);
    });
    select.value = currentVal;
  }

  async function handleCreateNotebook() {
    el.notebookErrorMsg.style.display = 'none';
    const name = el.notebookNameInput.value.trim();
    const description = el.notebookDescInput.value.trim();

    if (!name) {
      el.notebookErrorMsg.textContent = 'Notebook name is required.';
      el.notebookErrorMsg.style.display = 'block';
      return;
    }

    try {
      await apiFetch('/api/notebooks', {
        method: 'POST',
        body: JSON.stringify({ name, description: description || null }),
      });
      el.notebookModal.style.display = 'none';
      el.notebookNameInput.value = '';
      el.notebookDescInput.value = '';
      await loadNotebooks();
    } catch (err) {
      el.notebookErrorMsg.textContent = err.message;
      el.notebookErrorMsg.style.display = 'block';
    }
  }

  // --- Sources & Ingestion ---
  async function loadSources() {
    if (!state.token) return;
    try {
      const sources = await apiFetch('/api/sources');
      state.sources = sources;
      renderSources(sources);
      checkPolling(sources);
    } catch (err) {
      console.error('Failed to load sources:', err);
    }
  }

  function renderSources(sources) {
    const container = el.sourcesGrid;
    container.innerHTML = '';

    if (!sources || sources.length === 0) {
      container.innerHTML = `
        <div class="empty-state">
          <p>No documents uploaded yet. Drop a PDF or Markdown file above to start studying!</p>
        </div>`;
      return;
    }

    sources.forEach(src => {
      const card = document.createElement('div');
      card.className = `source-card status-${src.status.toLowerCase()}`;
      card.id = `source-${src.id}`;

      const icon = getFileIcon(src.contentType, src.fileName);
      const formattedSize = formatBytes(src.sizeBytes);
      const formattedDate = new Date(src.createdAtUtc).toLocaleDateString(undefined, {
        month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
      });

      let statusBadge = `<span class="status-badge badge-${src.status.toLowerCase()}">${src.status}</span>`;
      let failureHtml = '';
      let retryHtml = '';

      if (src.status === 'Processing') {
        statusBadge = `<span class="status-badge badge-processing"><span class="spinner-sm"></span> Processing</span>`;
      } else if (src.status === 'Failed') {
        failureHtml = `<div class="failure-reason" title="${escapeHtml(src.failureReason || '')}">⚠️ ${escapeHtml(src.failureReason || 'Ingestion failed')}</div>`;
        retryHtml = `<button class="btn-retry btn-sm" onclick="window.retrySource('${src.id}')">🔄 Retry</button>`;
      }

      const fileUrl = getSourceFileUrl(src.id);

      card.innerHTML = `
        <div class="source-card-header">
          <div class="source-type-icon">${icon}</div>
          <div class="source-header-meta">
            <h4 class="source-title" title="${escapeHtml(src.title)}">
              <a href="${fileUrl}" target="_blank" rel="noopener noreferrer" class="source-file-link" title="Open ${escapeHtml(src.fileName)} in new tab">${escapeHtml(src.title)}</a>
            </h4>
            <a href="${fileUrl}" target="_blank" rel="noopener noreferrer" class="source-filename-link" title="Open ${escapeHtml(src.fileName)} in new tab">${escapeHtml(src.fileName)} ↗</a>
          </div>
          ${statusBadge}
        </div>
        <div class="source-card-body">
          <div class="meta-row">
            <span>Size: <strong>${formattedSize}</strong></span>
            <span>Chunks: <strong>${src.chunkCount}</strong></span>
          </div>
          <div class="meta-row">
            <span>Uploaded: ${formattedDate}</span>
          </div>
          ${failureHtml}
        </div>
        <div class="source-card-footer">
          <div class="notebook-assign">
            <select class="source-nb-select" onchange="window.assignSourceNotebook('${src.id}', this.value)">
              <option value="">+ Assign Notebook</option>
              ${state.notebooks.map(nb => `<option value="${nb.id}">${escapeHtml(nb.name)}</option>`).join('')}
            </select>
          </div>
          ${retryHtml}
        </div>
      `;
      container.appendChild(card);
    });
  }

  window.retrySource = async (id) => {
    try {
      await apiFetch(`/api/sources/${id}/retry`, { method: 'POST' });
      await loadSources();
    } catch (err) {
      alert(`Retry failed: ${err.message}`);
    }
  };

  window.assignSourceNotebook = async (sourceId, notebookId) => {
    if (!notebookId) return;
    try {
      await apiFetch(`/api/notebooks/${notebookId}/sources/${sourceId}`, { method: 'PUT' });
      await loadNotebooks();
      alert('Document assigned to notebook!');
    } catch (err) {
      alert(`Could not assign notebook: ${err.message}`);
    }
  };

  function checkPolling(sources) {
    const hasPendingOrProcessing = sources.some(s => s.status === 'Pending' || s.status === 'Processing');
    if (hasPendingOrProcessing && !state.pollTimer) {
      state.pollTimer = setInterval(async () => {
        const updated = await apiFetch('/api/sources');
        state.sources = updated;
        renderSources(updated);
        const stillWorking = updated.some(s => s.status === 'Pending' || s.status === 'Processing');
        if (!stillWorking) {
          stopPolling();
          loadNotebooks();
        }
      }, 3000);
    } else if (!hasPendingOrProcessing && state.pollTimer) {
      stopPolling();
    }
  }

  function stopPolling() {
    if (state.pollTimer) {
      clearInterval(state.pollTimer);
      state.pollTimer = null;
    }
  }

  async function handleFileUpload(file) {
    if (!state.token) {
      showAuthModal(true);
      return;
    }

    if (!file) return;

    el.uploadProgress.style.display = 'flex';
    el.uploadStatusText.textContent = `Uploading "${file.name}" (${formatBytes(file.size)})...`;

    const formData = new FormData();
    formData.append('file', file);

    try {
      await apiFetch('/api/sources', {
        method: 'POST',
        body: formData,
      });

      el.uploadStatusText.textContent = 'Upload complete! Ingestion started.';
      setTimeout(() => {
        el.uploadProgress.style.display = 'none';
      }, 1500);

      await loadSources();
    } catch (err) {
      el.uploadProgress.style.display = 'none';
      alert(`Upload failed: ${err.message}`);
    }
  }

  // --- Search & Results ---
  async function handleSearch() {
    const query = el.searchInput.value.trim();
    if (!query) return;

    if (!state.token) {
      showAuthModal(true);
      return;
    }

    el.searchSubmitBtn.disabled = true;
    el.searchSubmitBtn.textContent = 'Searching...';
    el.summarySection.style.display = 'none';

    const mode = state.settings.mode || 'hybrid';
    const notebookId = el.searchNotebookSelect.value;
    const expand = !!state.settings.expand;
    const limit = state.settings.limit || 20;

    const payload = {
      query,
      mode,
      expand,
      notebookIds: notebookId ? [notebookId] : null,
      conversationId: state.currentConversationId || null,
      limit
    };

    try {
      const response = await apiFetch('/api/search', {
        method: 'POST',
        body: JSON.stringify(payload),
      });

      state.currentConversationId = response.conversationId;
      state.currentResults = response;

      renderSearchResults(response);
    } catch (err) {
      alert(`Search error: ${err.message}`);
    } finally {
      el.searchSubmitBtn.disabled = false;
      el.searchSubmitBtn.textContent = 'Search';
    }
  }

  function renderSearchResults(results) {
    el.resultsHeader.style.display = 'flex';
    el.resultsQueryTitle.textContent = `Results for "${results.query}"`;
    const container = el.passagesGroups;
    container.innerHTML = '';

    if (!results.groups || results.groups.length === 0) {
      container.innerHTML = `
        <div class="empty-state">
          <p>No matching passages found. Try a different query or switch retrieval mode to 'Hybrid'.</p>
        </div>`;
      return;
    }

    results.groups.forEach(group => {
      const groupEl = document.createElement('div');
      groupEl.className = 'passage-group';

      const fileIcon = getFileIcon(group.source.contentType, group.source.title);
      const groupFileUrl = getSourceFileUrl(group.source.id);

      groupEl.innerHTML = `
        <div class="group-header">
          <a href="${groupFileUrl}" target="_blank" rel="noopener noreferrer" class="group-source-link" title="Open ${escapeHtml(group.source.title)} in new tab">
            <span class="group-icon">${fileIcon}</span>
            <span class="group-title">${escapeHtml(group.source.title)} ↗</span>
          </a>
          <span class="group-count">${group.passages.length} ${group.passages.length === 1 ? 'passage' : 'passages'}</span>
        </div>
        <div class="group-passages">
          ${group.passages.map(p => {
            const citNum = p.number;
            const pageUrl = getSourceFileUrl(group.source.id, p.pageNumber, p.snippet);
            const pageLabel = p.pageLabel ? escapeHtml(p.pageLabel) : p.pageNumber;
            const pageBadge = `<a href="${pageUrl}" target="_blank" rel="noopener noreferrer" class="badge-page-link" title="Open ${escapeHtml(group.source.title)} at page ${p.pageNumber} in new tab">Page ${pageLabel} ↗</a>`;
            const sectionBadge = p.section ? `<span class="badge-section">${escapeHtml(p.section)}</span>` : '';
            const matchBadges = (p.matchedBy || []).map(m => `<span class="badge-match match-${m}">${m}</span>`).join(' ');

            return `
              <div class="passage-card" id="passage-hit-${citNum}" data-citation-num="${citNum}">
                <div class="passage-meta">
                  <div class="passage-location">
                    <span class="citation-index">[${citNum}]</span>
                    ${pageBadge}
                    ${sectionBadge}
                  </div>
                  <div class="passage-scoring">
                    ${matchBadges}
                    <span class="passage-score" title="RRF / Hybrid Score">Score: ${p.score.toFixed(4)}</span>
                  </div>
                </div>
                <div class="passage-snippet">${renderSnippetHtml(p.snippet)}</div>
              </div>
            `;
          }).join('')}
        </div>
      `;
      container.appendChild(groupEl);
    });
  }

  // --- Grounded AI Summary Streaming via SSE ---
  async function handleGenerateSummary() {
    if (!state.currentConversationId) {
      alert('Please perform a search first.');
      return;
    }

    el.generateSummaryBtn.disabled = true;
    el.generateSummaryBtn.textContent = '✨ Generating...';
    el.summarySection.style.display = 'block';
    el.summaryAnswer.innerHTML = '<span class="summary-streaming-indicator">Thinking and synthesizing from sources...</span>';
    el.summaryCitationsList.textContent = 'Streaming citations...';
    el.summarySection.scrollIntoView({ behavior: 'smooth', block: 'start' });

    try {
      const response = await fetch(`/api/search/${state.currentConversationId}/summary/stream`, {
        headers: {
          'Authorization': `Bearer ${state.token}`
        }
      });

      if (!response.ok) {
        throw new Error(`Summary streaming failed: ${response.statusText}`);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder('utf-8');
      let accumulatedText = '';
      let citedPassages = [];
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop();

        for (const line of lines) {
          const trimmed = line.trim();
          if (trimmed.startsWith('data: ')) {
            const dataStr = trimmed.slice(6);
            try {
              const data = JSON.parse(dataStr);
              if (data.token) {
                accumulatedText += data.token;
                el.summaryAnswer.innerHTML = formatAnswerWithCitations(accumulatedText);
              }
              if (data.done && data.cited) {
                citedPassages = data.cited;
              }
              if (data.error) {
                throw new Error(data.error);
              }
            } catch (err) {
              if (err.message && !err.message.includes('JSON')) throw err;
            }
          }
        }
      }

      el.summaryAnswer.innerHTML = formatAnswerWithCitations(accumulatedText);
      if (citedPassages && citedPassages.length > 0) {
        el.summaryCitationsList.innerHTML = citedPassages
          .map(num => `<a href="#passage-hit-${num}" class="citation-pill" onclick="window.scrollToCitation(${num}, event)">[${num}]</a>`)
          .join(' ');
      } else {
        el.summaryCitationsList.textContent = 'None directly cited in answer.';
      }
    } catch (err) {
      alert(`Summary generation failed: ${err.message}`);
    } finally {
      el.generateSummaryBtn.disabled = false;
      el.generateSummaryBtn.textContent = '✨ Generate AI Summary';
    }
  }

  function renderSummary(summary) {
    el.summarySection.style.display = 'block';

    // Format answer with clickable citations [1]
    const formattedAnswer = formatAnswerWithCitations(summary.answer);
    el.summaryAnswer.innerHTML = formattedAnswer;

    // Render citations footer list
    if (summary.citedPassages && summary.citedPassages.length > 0) {
      el.summaryCitationsList.innerHTML = summary.citedPassages
        .map(num => `<a href="#passage-hit-${num}" class="citation-pill" onclick="window.scrollToCitation(${num}, event)">[${num}]</a>`)
        .join(' ');
    } else {
      el.summaryCitationsList.textContent = 'None directly cited in answer.';
    }

    // Scroll summary into view smoothly
    el.summarySection.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  function formatAnswerWithCitations(text) {
    // Escape HTML first
    let escaped = escapeHtml(text);

    // Replace [1], [2], etc. with clickable spans
    escaped = escaped.replace(/\[(\d+)\]/g, (match, num) => {
      return `<button class="inline-citation" onclick="window.scrollToCitation(${num}, event)">[${num}]</button>`;
    });

    // Simple markdown paragraph conversion
    return escaped
      .split(/\n\s*\n/)
      .map(p => `<p>${p.replace(/\n/g, '<br>')}</p>`)
      .join('');
  }

  window.scrollToCitation = (citNum, ev) => {
    if (ev) ev.preventDefault();
    const hitEl = document.getElementById(`passage-hit-${citNum}`);
    if (hitEl) {
      hitEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
      hitEl.classList.add('highlight-flash');
      setTimeout(() => hitEl.classList.remove('highlight-flash'), 2000);
    }
  };

  // --- Voice / Speech Recognition (Hands-Free) ---
  function initSpeechRecognition() {
    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SpeechRecognition) {
      el.voiceSearchBtn.title = 'Speech Recognition not supported in this browser.';
      el.voiceSearchBtn.style.opacity = '0.5';
      return;
    }

    const recognition = new SpeechRecognition();
    recognition.continuous = false;
    recognition.interimResults = true;
    recognition.lang = 'en-US';

    recognition.onstart = () => {
      state.isRecording = true;
      el.voiceSearchBtn.classList.add('recording');
      el.searchInput.placeholder = 'Listening to your question...';
    };

    recognition.onresult = (event) => {
      let transcript = '';
      for (let i = event.resultIndex; i < event.results.length; ++i) {
        transcript += event.results[i][0].transcript;
      }
      el.searchInput.value = transcript;
    };

    recognition.onerror = (event) => {
      console.warn('Speech recognition error:', event.error);
      stopRecording();
    };

    recognition.onend = () => {
      stopRecording();
      if (el.searchInput.value.trim().length > 0) {
        handleSearch();
      }
    };

    state.speechRecognition = recognition;
  }

  function toggleVoiceRecording() {
    if (!state.speechRecognition) {
      alert('Speech Recognition is not supported in this browser (use Chrome/Safari/Edge).');
      return;
    }

    if (state.isRecording) {
      state.speechRecognition.stop();
      stopRecording();
    } else {
      try {
        state.speechRecognition.start();
      } catch (err) {
        console.error('Speech recognition start failed:', err);
      }
    }
  }

  function stopRecording() {
    state.isRecording = false;
    el.voiceSearchBtn.classList.remove('recording');
    el.searchInput.placeholder = "Ask a question or search passages (e.g. 'what is the difference between TD and Monte Carlo?')";
  }

  // --- Text-to-Speech (Read Aloud) ---
  function toggleSpeechSummary() {
    if (!('speechSynthesis' in window)) {
      alert('Text-to-speech synthesis not supported in this browser.');
      return;
    }

    if (state.isSpeaking) {
      window.speechSynthesis.cancel();
      state.isSpeaking = false;
      el.speakSummaryBtn.textContent = '🔊 Read Aloud';
      return;
    }

    const text = el.summaryAnswer.textContent || '';
    if (!text.trim()) return;

    // Clean up citation brackets "[1]" before speaking so it sounds natural
    const cleanSpokenText = text.replace(/\[\d+\]/g, '');

    const utterance = new SpeechSynthesisUtterance(cleanSpokenText);
    utterance.rate = 1.0;
    utterance.pitch = 1.0;

    utterance.onstart = () => {
      state.isSpeaking = true;
      el.speakSummaryBtn.textContent = '⏹️ Stop Reading';
    };

    utterance.onend = () => {
      state.isSpeaking = false;
      el.speakSummaryBtn.textContent = '🔊 Read Aloud';
    };

    utterance.onerror = () => {
      state.isSpeaking = false;
      el.speakSummaryBtn.textContent = '🔊 Read Aloud';
    };

    window.speechSynthesis.speak(utterance);
  }

  // --- History View ---
  async function loadHistory() {
    if (!state.token) return;
    try {
      const conversations = await apiFetch('/api/conversations');
      renderHistoryList(conversations);
    } catch (err) {
      console.error('Failed to load history:', err);
    }
  }

  function renderHistoryList(conversations) {
    const container = el.historyList;
    container.innerHTML = '';

    if (!conversations || conversations.length === 0) {
      container.innerHTML = '<div class="empty-state">No search history recorded yet.</div>';
      return;
    }

    conversations.forEach(conv => {
      const item = document.createElement('div');
      item.className = 'history-item';
      item.dataset.id = conv.id;

      const dateStr = new Date(conv.updatedAtUtc).toLocaleDateString(undefined, {
        month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
      });

      item.innerHTML = `
        <div class="history-item-title">${escapeHtml(conv.title)}</div>
        <div class="history-item-date">${dateStr}</div>
      `;

      item.addEventListener('click', () => loadConversationDetails(conv.id));
      container.appendChild(item);
    });
  }

  async function loadConversationDetails(id) {
    // Highlight active history item
    document.querySelectorAll('.history-item').forEach(item => {
      item.classList.toggle('active', item.dataset.id === id);
    });

    try {
      const conv = await apiFetch(`/api/conversations/${id}`);
      renderConversationDetails(conv);
    } catch (err) {
      alert(`Failed to load conversation: ${err.message}`);
    }
  }

  function renderConversationDetails(conv) {
    const container = el.historyDetails;
    container.innerHTML = `
      <div class="conv-header">
        <h3>${escapeHtml(conv.title)}</h3>
        <span class="conv-date">${new Date(conv.createdAtUtc).toLocaleString()}</span>
      </div>
      <div class="conv-messages">
        ${conv.messages.map(msg => {
          const isUser = msg.role === 'user';
          const citationsHtml = (!isUser && msg.citations && msg.citations.length > 0)
            ? `<div class="msg-citations">
                 <strong>Citations:</strong>
                 ${msg.citations.map(c => `
                   <div class="citation-detail-item">
                     <span class="citation-badge">[${c.number}]</span>
                     <span class="citation-loc">${c.pageLabel ? `Page ${escapeHtml(c.pageLabel)}` : (c.pageNumber ? `Page ${c.pageNumber}` : '')} ${c.section ? `• ${escapeHtml(c.section)}` : ''}</span>
                     <p class="citation-excerpt">${escapeHtml(c.content)}</p>
                   </div>
                 `).join('')}
               </div>`
            : '';

          return `
            <div class="conv-message role-${msg.role}">
              <div class="message-meta">${isUser ? '👤 You' : '🤖 Assistant'}</div>
              <div class="message-text">${formatAnswerWithCitations(msg.content)}</div>
              ${citationsHtml}
            </div>
          `;
        }).join('')}
      </div>
    `;
  }

  // --- Utility Functions ---
  function escapeHtml(str) {
    if (!str) return '';
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function renderSnippetHtml(snippet) {
    if (!snippet) return '';
    return escapeHtml(snippet)
      .replaceAll('&lt;mark&gt;', '<mark>')
      .replaceAll('&lt;/mark&gt;', '</mark>');
  }

  function formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
  }

  function getFileIcon(contentType, fileName = '') {
    if (contentType?.includes('pdf') || fileName.endsWith('.pdf')) return '📄';
    if (contentType?.includes('markdown') || fileName.endsWith('.md')) return '📝';
    return '📃';
  }

  async function loadAllData() {
    await Promise.all([
      loadNotebooks(),
      loadSources(),
    ]);
  }

  // --- Settings UI ---
  function updateSettingsUI() {
    el.settingsModeSelect.value = state.settings.mode || 'hybrid';
    el.settingsLimitInput.value = state.settings.limit || '20';
    el.settingsExpandCheckbox.checked = !!state.settings.expand;
    el.settingsCutoffInput.value = state.settings.cutoff || '0.7';

    const modeName = state.settings.mode.charAt(0).toUpperCase() + state.settings.mode.slice(1);
    const expandNote = state.settings.expand ? ' + Expansion' : '';
    el.settingsSummaryPill.textContent = `${modeName}${expandNote} • Top ${state.settings.limit}`;
  }

  function handleSaveSettings() {
    state.settings.mode = el.settingsModeSelect.value;
    state.settings.limit = parseInt(el.settingsLimitInput.value, 10) || 20;
    state.settings.expand = el.settingsExpandCheckbox.checked;
    state.settings.cutoff = parseFloat(el.settingsCutoffInput.value) || 0.7;
    localStorage.setItem('talk_to_rag_settings', JSON.stringify(state.settings));
    updateSettingsUI();
    closeModal(el.settingsModal);
  }

  // --- Event Listeners Setup ---
  function setupEventListeners() {
    // Navigation
    el.tabs.forEach(tab => {
      tab.addEventListener('click', () => switchTab(tab.dataset.view));
    });

    // Dialog light dismiss fallbacks
    setupDialogLightDismiss(el.authModal);
    setupDialogLightDismiss(el.notebookModal);
    setupDialogLightDismiss(el.settingsModal);

    // Auth
    el.openAuthBtn.addEventListener('click', () => showAuthModal(true, 'login'));
    el.closeAuthModalBtn.addEventListener('click', () => showAuthModal(false));
    el.signOutBtn.addEventListener('click', signOut);
    el.authSubmitBtn.addEventListener('click', handleAuthSubmit);
    el.authToggleLink.addEventListener('click', (e) => {
      e.preventDefault();
      showAuthModal(true, authMode === 'login' ? 'register' : 'login');
    });

    // Enter key inside auth modal
    el.authPassword.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') handleAuthSubmit();
    });

    // Settings
    el.openSettingsBtn.addEventListener('click', () => {
      updateSettingsUI();
      openModal(el.settingsModal);
    });
    el.closeSettingsModalBtn.addEventListener('click', () => closeModal(el.settingsModal));
    el.saveSettingsBtn.addEventListener('click', handleSaveSettings);

    // Notebook modal
    el.newNotebookBtn.addEventListener('click', () => {
      el.notebookErrorMsg.style.display = 'none';
      openModal(el.notebookModal);
      el.notebookNameInput.focus();
    });
    el.closeNotebookModalBtn.addEventListener('click', () => {
      closeModal(el.notebookModal);
    });
    el.createNotebookSubmitBtn.addEventListener('click', handleCreateNotebook);
    el.notebookNameInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') handleCreateNotebook();
    });

    // Sources dropzone & upload
    el.refreshSourcesBtn.addEventListener('click', loadSources);
    el.browseFilesBtn.addEventListener('click', () => el.fileInput.click());
    el.fileInput.addEventListener('change', (e) => {
      if (e.target.files && e.target.files.length > 0) {
        handleFileUpload(e.target.files[0]);
      }
    });

    el.dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      el.dropzone.classList.add('dragover');
    });

    el.dropzone.addEventListener('dragleave', () => {
      el.dropzone.classList.remove('dragover');
    });

    el.dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      el.dropzone.classList.remove('dragover');
      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        handleFileUpload(e.dataTransfer.files[0]);
      }
    });

    // Search
    el.searchSubmitBtn.addEventListener('click', handleSearch);
    el.searchInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') handleSearch();
    });

    // Voice recognition
    el.voiceSearchBtn.addEventListener('click', toggleVoiceRecording);

    // Summary & Speech
    el.generateSummaryBtn.addEventListener('click', handleGenerateSummary);
    el.speakSummaryBtn.addEventListener('click', toggleSpeechSummary);

    // History
    el.refreshHistoryBtn.addEventListener('click', loadHistory);
  }

  // --- Initialize App ---
  async function init() {
    setupEventListeners();
    updateSettingsUI();
    initSpeechRecognition();
    updateAuthUI();

    if (state.token) {
      try {
        // Validate token with Me endpoint
        const me = await apiFetch('/api/auth/me');
        state.user = me;
        localStorage.setItem('talk_to_rag_user', JSON.stringify(me));
        updateAuthUI();
        await loadAllData();
      } catch {
        signOut();
      }
    }
  }

  init();
})();
