'use strict';

const $ = id => document.getElementById(id);
const app = { data: null, selected: localStorage.getItem('coi.selected'), tab: 'overview', logs: [], cursor: 0, logStreamId: null, saves: null, busy: new Set(), statePolling: false, logPolling: false, savesPolling: false, settingsFor: null, settingsSnapshot: null, settingsDirty: false, confirm: null, logRevision: 0, renderedLogRevision: -1 };
const number = value => typeof value === 'number' ? value.toLocaleString() : '—';
const text = (id, value) => { const node = $(id); if (node.textContent !== String(value)) node.textContent = value; };
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const titleCase = value => String(value).replace(/\b\w/g, letter => letter.toUpperCase());
const profile = () => app.data?.profiles.find(item => item.id === app.selected);
const runtime = () => app.data?.servers.find(item => item.id === app.selected) || { state: 'stopped' };
const editable = run => ['stopped', 'faulted'].includes(run.state) && !run.processId;
const live = run => ['running', 'paused'].includes(run.state);
const serverUrl = (suffix = '', id = app.selected) => `/api/servers/${encodeURIComponent(id)}${suffix}`;
const containerMode = () => app.data?.environment.containerMode === true;
const gamePort = item => containerMode() ? app.data.environment.publicGamePort : item.gamePort;
const fixedContainerFields = new Set(['bindAddress', 'gamePort', 'controlPort', 'gameDirectory']);
const iconServer = '<svg class="profile-icon" viewBox="0 0 20 20" aria-hidden="true"><rect x="3" y="2" width="14" height="6" rx="1.5"/><rect x="3" y="12" width="14" height="6" rx="1.5"/><path d="M6 5h.1M6 15h.1m4-10h4m-4 10h4"/></svg>';

function bytes(value) { if (typeof value !== 'number') return '—'; if (value < 1024) return `${value} B`; const unit = value < 1048576 ? 'KB' : value < 1073741824 ? 'MB' : 'GB'; const divisor = unit === 'KB' ? 1024 : unit === 'MB' ? 1048576 : 1073741824; return `${(value / divisor).toLocaleString(undefined, { maximumFractionDigits: 1 })} ${unit}`; }
function duration(seconds) { if (typeof seconds !== 'number') return '—'; const mins = Math.floor(seconds / 60), hours = Math.floor(mins / 60), days = Math.floor(hours / 24); return days ? `${days}d ${hours % 24}h` : hours ? `${hours}h ${mins % 60}m` : `${mins}m ${Math.floor(seconds % 60)}s`; }
function date(value) { if (!value) return 'Not yet saved'; const parsed = new Date(value); return Number.isNaN(parsed.valueOf()) ? 'Unknown time' : parsed.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }); }
function time(value) { const parsed = new Date(value); return Number.isNaN(parsed.valueOf()) ? '—' : parsed.toLocaleTimeString(undefined, { hour12: false }); }
function toast(message, error = false) { $('toast-region').replaceChildren(); const item = document.createElement('div'); item.className = `toast${error ? ' error' : ''}`; item.textContent = message; $('toast-region').append(item); clearTimeout(app.toastTimer); app.toastTimer = setTimeout(() => item.remove(), error ? 9000 : 4500); }
function setNotice(id, message) { text(id, message || ''); $(id).hidden = !message; }

async function api(url, options = {}) {
  const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), options.timeout || 90000);
  try {
    const headers = { ...(options.headers || {}) };
    if (options.method && options.method !== 'GET') headers['X-Manager-Request'] = '1';
    let body = options.body;
    if (body && !(body instanceof Blob)) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(body); }
    const response = await fetch(url, { ...options, body, headers, signal: controller.signal, cache: 'no-store', credentials: 'same-origin' });
    if (response.status === 401) { location.replace('/login.html'); throw new Error('Your session has ended. Sign in again to continue.'); }
    const result = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new Error(result?.error || `Request failed (${response.status}).`);
    return result;
  } catch (error) { if (error.name === 'AbortError') throw new Error('The request timed out. Check the server console before trying again.'); throw error; }
  finally { clearTimeout(timeout); }
}

async function pollState() {
  if (app.statePolling) return; app.statePolling = true;
  try {
    app.data = await api('/api/state', { timeout: 12000 });
    if (!app.data.profiles.some(item => item.id === app.selected)) selectProfile(app.data.profiles[0]?.id || null, false);
    $('loading-state').hidden = true; $('empty-state').hidden = app.data.profiles.length !== 0; $('server-view').hidden = app.data.profiles.length === 0;
    setNotice('connection-error', ''); text('manager-status', 'Manager connected'); $('manager-led').classList.remove('offline');
    text('host-name', app.data.environment.hostName || 'Local workspace'); text('version', `v${app.data.version}`);
    $('create-profile').hidden = containerMode(); $('empty-create').hidden = containerMode();
    $('logout').hidden = !app.data.environment.authenticationRequired;
    text('manager-location', containerMode() ? 'Container manager' : app.data.environment.authenticationRequired ? 'LAN manager' : 'Local manager');
    renderProfiles(); if (profile()) renderServer();
  } catch (error) { $('loading-state').hidden = true; setNotice('connection-error', `Manager unavailable. ${error.message} Reconnecting automatically…`); text('manager-status', 'Connection interrupted'); $('manager-led').classList.add('offline'); }
  finally { app.statePolling = false; }
}

function selectProfile(id, render = true) {
  if (id === app.selected && app.settingsFor === id) return;
  app.selected = id; localStorage.setItem('coi.selected', id || ''); app.logs = []; app.cursor = 0; app.logStreamId = null; app.logRevision++; app.saves = null; app.settingsFor = null; app.settingsSnapshot = null; app.settingsDirty = false;
  $('log-search').value = ''; $('console-output').innerHTML = '<div class="console-empty">Waiting for server output.</div>';
  if (render && profile()) { renderProfiles(); renderServer(); pollLogs(); pollSaves(); }
}

function renderProfiles() {
  text('profile-count', app.data.profiles.length);
  const html = app.data.profiles.map(item => { const run = app.data.servers.find(value => value.id === item.id) || { state: 'stopped' }; return `<button class="profile-button${item.id === app.selected ? ' active' : ''}" type="button" data-profile="${escapeHtml(item.id)}" ${item.id === app.selected ? 'aria-current="page"' : ''}>${iconServer}<span><span class="profile-title">${escapeHtml(item.name)}</span><span class="profile-subtitle">${escapeHtml(item.map)} · ${escapeHtml(titleCase(run.state))}</span></span><span class="profile-state-dot ${escapeHtml(run.state)}" aria-hidden="true"></span></button>`; }).join('');
  if ($('profile-list').innerHTML !== html) $('profile-list').innerHTML = html;
}

function renderServer() {
  const item = profile(), run = runtime(), world = run.world, pending = app.busy.has(item.id), active = live(run), stopped = editable(run);
  text('breadcrumb-name', item.name); text('server-name', item.name); text('server-map', item.map); text('server-endpoint', `${containerMode() ? location.hostname : item.bindAddress}:${gamePort(item)}`); text('server-process', run.processId ? `PID ${run.processId}` : '');
  document.title = `${item.name} · CoI Server Manager`;
  text('server-status', titleCase(run.state)); $('server-status').className = `status-badge ${run.state}`;
  const start = $('start-stop'); start.dataset.action = stopped ? 'start' : 'stop';
  start.innerHTML = stopped ? '<svg viewBox="0 0 20 20" aria-hidden="true"><path d="m6 4 10 6-10 6Z"/></svg> Start server' : '<svg viewBox="0 0 20 20" aria-hidden="true"><rect x="5" y="5" width="10" height="10" rx="1"/></svg> Stop server';
  start.disabled = pending || run.state === 'stopping' || (!stopped && !run.isOwned) || (stopped && app.data.environment.hostBuildExists === false);
  $('restart-server').disabled = pending || !active || !run.isOwned;
  const pause = $('pause-resume'); pause.dataset.action = run.state === 'paused' ? 'resume' : 'pause'; text('pause-resume', run.state === 'paused' ? 'Resume simulation' : 'Pause simulation'); pause.disabled = pending || !active;
  $('save-now').disabled = pending || !active;
  setNotice('server-alert', run.lastError);
  const info = run.state === 'starting' ? 'Starting the simulation and loading the island. Large saves can take a little while.' : run.state === 'stopping' ? 'Saving the island and shutting down the server…' : run.joining ? 'A player is loading the island. Simulation briefly waits for their client to catch up.' : !run.isOwned && run.processId ? 'This server process was started outside the manager. Live status is available; process controls are limited.' : app.data.environment.hostBuildExists === false ? 'The dedicated server has not been built yet. Build it before starting a profile.' : '';
  setNotice('server-info', info);
  text('stat-players', number(run.players)); text('players-note', run.joining ? 'A player is joining' : active ? 'Connected to this island' : 'Waiting for server');
  text('stat-population', number(world?.Population ?? world?.population));
  const capacity = world?.HousingCapacity ?? world?.housingCapacity; text('population-note', typeof capacity === 'number' ? `${number(capacity)} total housing capacity` : 'Waiting for island data');
  text('stat-step', number(run.step)); text('step-note', run.state === 'paused' ? 'Simulation is paused' : active ? 'World advancing independently' : run.step != null ? 'Last reported simulation step' : 'Persistent world simulation');
  text('stat-uptime', duration(run.uptimeSeconds)); text('uptime-note', run.startedUtc ? `Started ${date(run.startedUtc)}` : 'This session');
  text('simulation-label', run.state === 'running' ? 'Running' : run.state === 'paused' ? 'Paused' : run.state === 'starting' ? 'Loading island' : run.state === 'stopping' ? 'Saving & stopping' : run.state === 'unresponsive' ? 'Not responding' : 'Offline');
  text('world-entities', number(world?.EntityCount ?? world?.entityCount)); text('world-settlements', number(world?.SettlementCount ?? world?.settlementCount));
  text('process-memory', bytes(run.memoryBytes)); text('process-cpu', typeof run.cpuPercent === 'number' ? `${run.cpuPercent.toFixed(1)}%` : '—');
  text('autosave-label', `Autosave every ${item.autosaveSeconds >= 60 && item.autosaveSeconds % 60 === 0 ? `${item.autosaveSeconds / 60} min` : `${item.autosaveSeconds} sec`}`);
  renderConnection(item, run); renderInventory(world); renderRecent();
  const settingsSnapshot = JSON.stringify(item);
  if (app.settingsFor !== item.id || (!app.settingsDirty && app.settingsSnapshot !== settingsSnapshot)) {
    $('settings-fields').innerHTML = fieldsHtml(item, 'settings'); app.settingsFor = item.id; app.settingsSnapshot = settingsSnapshot; app.settingsDirty = false;
  }
  $('settings-fields').querySelectorAll('input,select').forEach(input => {
    const fixed = containerMode() && fixedContainerFields.has(input.name);
    input.disabled = !stopped || pending || fixed;
    if (fixed) { input.value = String(item[input.name]); const help = input.parentElement.querySelector('small'); if (help) help.textContent = 'Managed by this Compose stack.'; }
  });
  $('save-settings').disabled = !stopped || pending; text('settings-lock', stopped ? 'READY TO EDIT' : 'STOP SERVER TO EDIT'); text('settings-help', app.settingsDirty && app.settingsSnapshot !== settingsSnapshot ? 'Settings changed elsewhere. Your unsaved edits are preserved. Reload this page to discard them and use the latest settings.' : stopped ? 'Changes take effect on the next start. Existing saves retain their map.' : 'Stop the server before changing its configuration.');
  $('import-save').disabled = !stopped || pending; $('create-backup').disabled = pending || !app.saves?.current?.exists;
  $('download-logs').href = serverUrl('/logs/download');
  if (app.tab === 'backups') renderSaves();
}

function renderConnection(item, run) {
  const local = ['127.0.0.1', '::1', 'localhost'].includes(item.bindAddress), all = ['0.0.0.0', '::'].includes(item.bindAddress);
  const addresses = containerMode() ? [location.hostname] : local ? [item.bindAddress] : all ? [...new Set([...(app.data.environment.addresses || []).filter(value => !['127.0.0.1', '::1'].includes(value)), '127.0.0.1'])] : [item.bindAddress];
  const html = addresses.map(address => `<option value="${escapeHtml(address)}">${escapeHtml(address)}</option>`).join('');
  if ($('connection-address').innerHTML !== html) { const previous = $('connection-address').value; $('connection-address').innerHTML = html; if (addresses.includes(previous)) $('connection-address').value = previous; }
  text('connection-mode', local ? 'LOCAL' : 'LAN / ONLINE'); text('connection-port', gamePort(item));
  text('network-note', containerMode() ? 'Invite trusted players over your LAN or a private VPN using this host and its published game port. Keep the manager access key private.' : local ? 'Only this PC can connect. Choose LAN access in Settings to invite other computers.' : 'Invite trusted players over your LAN or a private VPN. Share only the game port; keep the manager on your trusted network.');
  const key = $('download-key'); key.href = serverUrl('/join-key'); key.classList.toggle('disabled-link', !run.processId && !app.saves?.current?.exists); key.setAttribute('aria-disabled', key.classList.contains('disabled-link') ? 'true' : 'false');
}

function renderInventory(world) {
  const products = world?.Products ?? world?.products ?? [];
  text('product-count', products.length ? `${products.length} PRODUCTS` : 'LIVE DATA');
  const sorted = [...products].sort((a, b) => (b.GlobalQuantity ?? b.globalQuantity ?? 0) - (a.GlobalQuantity ?? a.globalQuantity ?? 0)).slice(0, 8);
  const html = sorted.length ? sorted.map(product => { const id = product.ProductId ?? product.productId ?? ''; const name = ['Virtual_Upoints', 'Product_Virtual_Upoints'].includes(id) ? 'Unity' : id.replace(/^(?:(?:Product|Virtual)_)+/, '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[_-]/g, ' '); return `<div class="inventory-row"><span title="${escapeHtml(id)}">${escapeHtml(name)}</span><strong>${number(product.GlobalQuantity ?? product.globalQuantity)}</strong></div>`; }).join('') : `<p class="panel-empty">${world ? 'No product quantities have been reported for this island.' : 'Start the server to see island inventory.'}</p>`;
  if ($('inventory').innerHTML !== html) $('inventory').innerHTML = html;
}

function setTab(tab, focus = false) {
  app.tab = tab; document.querySelectorAll('[data-tab]').forEach(button => { const selected = button.dataset.tab === tab; button.classList.toggle('active', selected); button.setAttribute('aria-selected', String(selected)); button.tabIndex = selected ? 0 : -1; if (selected && focus) button.focus(); });
  document.querySelectorAll('.tab-panel').forEach(panel => panel.hidden = panel.id !== `panel-${tab}`);
  if (tab === 'console') renderLogs(true);
  if (tab === 'backups') { renderSaves(); pollSaves(); }
}

async function pollLogs() {
  if (!app.selected || app.logPolling) return; app.logPolling = true; const id = app.selected;
  try {
    const data = await api(serverUrl(`/logs?after=${app.cursor}`, id), { timeout: 10000 }); if (app.selected !== id) return;
    if (app.logStreamId != null && data.streamId != null && data.streamId !== app.logStreamId) {
      app.logs = []; app.cursor = 0; app.logStreamId = data.streamId; app.logRevision++; renderRecent(); if (app.tab === 'console') renderLogs(); return;
    }
    app.logStreamId = data.streamId ?? app.logStreamId; app.cursor = data.next;
    if (data.entries?.length) { app.logs.push(...data.entries); app.logs = app.logs.slice(-1500); app.logRevision++; renderRecent(); if (app.tab === 'console') renderLogs(); }
  }
  catch { /* State polling supplies a visible connectivity warning. */ }
  finally { app.logPolling = false; }
}

function renderRecent() { const entries = app.logs.slice(-4).reverse(); const html = entries.length ? entries.map(entry => `<div class="activity-row ${escapeHtml(String(entry.level).toLowerCase())}"><time>${escapeHtml(time(entry.timestampUtc))}</time><span>${escapeHtml(entry.text)}</span></div>`).join('') : '<p class="panel-empty">No activity recorded yet. Start the server to begin.</p>'; if ($('recent-activity').innerHTML !== html) $('recent-activity').innerHTML = html; }
function renderLogs(force = false) {
  if (!force && app.renderedLogRevision === app.logRevision) return; app.renderedLogRevision = app.logRevision;
  const search = $('log-search').value.toLowerCase(), entries = app.logs.filter(entry => entry.text.toLowerCase().includes(search));
  const output = $('console-output'), scroll = output.scrollTop;
  output.innerHTML = entries.length ? entries.map(entry => `<div class="log-line ${escapeHtml(String(entry.level).toLowerCase())}"><span class="log-time">${escapeHtml(time(entry.timestampUtc))}</span><span class="log-text">${escapeHtml(entry.text)}</span></div>`).join('') : `<div class="console-empty">${search ? 'No lines match this filter.' : 'Waiting for server output.'}</div>`;
  text('log-count', `${number(entries.length)} lines`); output.scrollTop = $('autoscroll').checked ? output.scrollHeight : scroll;
}

async function pollSaves() {
  if (!app.selected || app.savesPolling) return; app.savesPolling = true; const id = app.selected;
  try { const result = await api(serverUrl('/saves', id), { timeout: 12000 }); if (app.selected !== id) return; app.saves = result; setNotice('save-notice', ''); renderSaves(); if (profile()) renderConnection(profile(), runtime()); }
  catch (error) { if (app.selected === id) setNotice('save-notice', `Could not read saves. ${error.message}`); }
  finally { app.savesPolling = false; }
}

function renderSaves() {
  if (!app.saves) return; const item = profile(); if (!item) return; const stopped = editable(runtime()), pending = app.busy.has(item.id), current = app.saves.current;
  $('current-save').innerHTML = current?.exists ? `<div><strong>Current island</strong><span>Last saved ${escapeHtml(date(current.modifiedUtc))}</span></div><span class="mono">island.save · ${bytes(current.bytes)}</span>` : '<div><strong>No island save yet</strong><span>The first server start creates an island from your selected map.</span></div>';
  $('backup-list').innerHTML = app.saves.backups?.length ? app.saves.backups.map(backup => `<div class="backup-row"><span class="backup-icon" aria-hidden="true"><svg viewBox="0 0 20 20"><path d="M4 2h8l4 4v12H4Zm8 0v5h4M7 11h6m-6 3h6"/></svg></span><div class="backup-body"><p class="backup-title">${escapeHtml(backup.name)}</p><div class="backup-meta"><span>${escapeHtml(date(backup.createdUtc))}</span><span class="mono">${bytes(backup.bytes)}</span><span class="mono" title="SHA-256: ${escapeHtml(backup.sha256)}">SHA ${escapeHtml(backup.sha256.slice(0, 8))}</span></div></div><div class="backup-actions"><a class="button secondary small" href="${serverUrl(`/backups/${encodeURIComponent(backup.id)}/download`)}">Download</a><button class="button secondary small" data-restore="${escapeHtml(backup.id)}" type="button" ${!stopped || pending ? 'disabled' : ''}>Restore</button></div></div>`).join('') : '<p class="panel-empty">No backups yet. Create one to keep a known good copy of your island.</p>';
  $('create-backup').disabled = pending || !current?.exists; $('import-save').disabled = pending || !stopped;
}

function fieldsHtml(item, prefix) {
  const maps = [...new Set([...(app.data?.environment.maps || []), item.map].filter(Boolean))];
  const bindOptions = [['127.0.0.1', 'This PC only'], ['0.0.0.0', 'LAN / online (all interfaces)']]; if (!bindOptions.some(([value]) => value === item.bindAddress)) bindOptions.push([item.bindAddress, item.bindAddress]);
  return `<section class="settings-section"><h3>Island identity</h3><div class="form-grid"><label class="form-field full" for="${prefix}-name">Server name<input id="${prefix}-name" name="name" value="${escapeHtml(item.name)}" maxlength="64" required autocomplete="off"></label><label class="form-field" for="${prefix}-map">Starting map<select id="${prefix}-map" name="map" required>${maps.map(map => `<option ${map === item.map ? 'selected' : ''}>${escapeHtml(map)}</option>`).join('')}</select><small>Used only when creating a new island.</small></label><label class="form-field" for="${prefix}-autosave">Autosave interval (seconds)<input id="${prefix}-autosave" name="autosaveSeconds" type="number" min="10" max="3600" value="${item.autosaveSeconds}" required><small>A fresh save is also written on shutdown.</small></label><label class="checkbox-field"><input name="startingFactory" type="checkbox" ${item.startingFactory ? 'checked' : ''}> Build the starting factory on a new island</label></div></section><section class="settings-section"><h3>Network</h3><div class="form-grid"><label class="form-field full" for="${prefix}-bind">Player access<select id="${prefix}-bind" name="bindAddress">${bindOptions.map(([value, label]) => `<option value="${escapeHtml(value)}" ${value === item.bindAddress ? 'selected' : ''}>${escapeHtml(label)}</option>`).join('')}</select><small>The manager stays local. Share or forward only the game port for players.</small></label><label class="form-field" for="${prefix}-gameport">Game port<input id="${prefix}-gameport" name="gamePort" type="number" min="1" max="65535" value="${item.gamePort}" required></label><label class="form-field" for="${prefix}-controlport">Administration port<input id="${prefix}-controlport" name="controlPort" type="number" min="1" max="65535" value="${item.controlPort}" required><small>Choose a different port for each server.</small></label></div></section><section class="settings-section"><h3>Local files</h3><div class="form-grid"><label class="form-field full" for="${prefix}-gamedir">Captain of Industry installation<input id="${prefix}-gamedir" name="gameDirectory" value="${escapeHtml(item.gameDirectory)}" required spellcheck="false"><small>The installed game supplies the simulation assemblies and maps.</small></label>${item.dataDirectory ? `<div class="form-field full">Server data directory<div class="path-value">${escapeHtml(item.dataDirectory)}</div><small>World saves and keys are isolated from your single-player saves.</small></div>` : ''}</div></section>`;
}

function readForm(form) { const data = new FormData(form); const fixed = containerMode() ? profile() : null; return { name: data.get('name').trim(), map: data.get('map'), gameDirectory: fixed?.gameDirectory ?? data.get('gameDirectory').trim(), bindAddress: fixed?.bindAddress ?? data.get('bindAddress'), controlPort: fixed?.controlPort ?? Number(data.get('controlPort')), gamePort: fixed?.gamePort ?? Number(data.get('gamePort')), autosaveSeconds: Number(data.get('autosaveSeconds')), startingFactory: data.get('startingFactory') === 'on' }; }
function openCreate() { if (!app.data) return toast('Wait for the manager to connect.', true); const used = new Set(app.data.profiles.flatMap(item => [item.controlPort, item.gamePort])); let control = 28550; while (used.has(control) || used.has(control + 1)) control += 2; const maps = app.data.environment.maps || []; const map = maps.find(name => name.replace(/\.map$/i, '') === 'New Haven') || maps[0] || 'New Haven'; $('create-fields').innerHTML = fieldsHtml({ name: app.data.profiles.length ? `Island ${app.data.profiles.length + 1}` : 'My island', gameDirectory: app.data.environment.gameDirectory || '', map, bindAddress: '127.0.0.1', controlPort: control, gamePort: control + 1, autosaveSeconds: 60, startingFactory: true }, 'create'); setNotice('create-error', ''); $('profile-dialog').showModal(); $('create-name').focus(); }

async function performAction(action, id = app.selected) {
  app.busy.add(id); if (id === app.selected) renderServer();
  try { const result = await api(serverUrl(`/actions/${action}`, id), { method: 'POST', body: {} }); toast(result?.message || `${titleCase(action)} requested.`); await pollState(); if (id === app.selected) await pollSaves(); }
  catch (error) { toast(error.message, true); }
  finally { app.busy.delete(id); if (profile()) renderServer(); }
}

function confirmAction(title, description, button, handler, name = false) { text('confirm-title', title); text('confirm-description', description); text('confirm-submit', button); $('backup-name-label').hidden = !name; $('backup-name').value = ''; setNotice('confirm-error', ''); app.confirm = handler; $('confirm-submit').disabled = false; $('confirm-dialog').showModal(); if (name) $('backup-name').focus(); }

async function downloadFile(url, fallbackName) {
  try {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
    if (response.status === 401) { location.replace('/login.html'); throw new Error('Sign in again to download this file.'); }
    if (!response.ok) { const result = await response.json().catch(() => null); throw new Error(result?.error || `Download failed (${response.status}).`); }
    const blob = await response.blob(), objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a'); link.href = objectUrl; link.download = fallbackName; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(objectUrl), 30000);
  } catch (error) { toast(error.message, true); }
}

document.addEventListener('click', event => {
  const download = event.target.closest('a[href]');
  if (download && /\/api\/servers\/[^/]+\/(?:join-key|logs\/download|backups\/[^/]+\/download)$/.test(download.pathname)) {
    event.preventDefault(); if (download.getAttribute('aria-disabled') !== 'true') downloadFile(download.href, download.pathname.endsWith('join-key') ? 'join.key' : download.pathname.includes('/logs/') ? 'server.log' : 'island-backup.save');
  }
  const selected = event.target.closest('[data-profile]'); if (selected) selectProfile(selected.dataset.profile);
  const tab = event.target.closest('[data-tab]'); if (tab) setTab(tab.dataset.tab);
  const openTab = event.target.closest('[data-open-tab]'); if (openTab) setTab(openTab.dataset.openTab, true);
  if (event.target.closest('[data-close-dialog]')) event.target.closest('dialog').close();
  const action = event.target.closest('[data-action]');
  if (action && !action.disabled) { const value = action.dataset.action, id = app.selected; if (value === 'restart') confirmAction('Restart this server?', 'The island will be saved, connected players will be disconnected, and the server will start again. Players can reconnect once it is running.', 'Restart server', async () => { $('confirm-dialog').close(); await performAction(value, id); }); else if (value === 'stop' && (runtime().players || 0) > 0) confirmAction('Stop this server?', `${runtime().players} player(s) are connected. The island will be saved before the server shuts down.`, 'Save & stop', async () => { $('confirm-dialog').close(); await performAction(value, id); }); else performAction(value); }
  const restore = event.target.closest('[data-restore]'); if (restore && !restore.disabled) { const id = app.selected, backup = app.saves.backups.find(item => item.id === restore.dataset.restore); confirmAction('Restore this backup?', `Restore “${backup.name}” from ${date(backup.createdUtc)}?\n\nThe current island will be backed up before it is replaced. The server stays stopped until you start it.`, 'Restore backup', async () => { await api(serverUrl(`/backups/${encodeURIComponent(backup.id)}/restore`, id), { method: 'POST', body: {} }); $('confirm-dialog').close(); toast('Backup restored. Start the server when you are ready.'); await pollSaves(); }); }
});

$('create-profile').addEventListener('click', openCreate); $('empty-create').addEventListener('click', openCreate);
$('logout').addEventListener('click', async () => { try { await api('/api/auth/logout', { method: 'POST', body: {} }); location.replace('/login.html'); } catch (error) { toast(error.message, true); } });
$('create-form').addEventListener('submit', async event => { event.preventDefault(); const button = $('create-submit'); button.disabled = true; setNotice('create-error', ''); try { const data = await api('/api/servers', { method: 'POST', body: readForm(event.target) }); $('profile-dialog').close(); await pollState(); const id = data?.id || data?.profile?.id || app.data.profiles.at(-1)?.id; selectProfile(id); toast('Server profile created. Start it to bring your island online.'); } catch (error) { setNotice('create-error', error.message); } finally { button.disabled = false; } });
$('settings-form').addEventListener('input', () => { app.settingsDirty = true; });
$('settings-form').addEventListener('change', () => { app.settingsDirty = true; });
$('settings-form').addEventListener('submit', async event => { event.preventDefault(); const id = app.selected; if (!editable(runtime())) return toast('Stop the server before editing its settings.', true); const input = readForm(event.target); app.busy.add(id); renderServer(); try { await api(serverUrl('', id), { method: 'PUT', body: input }); app.settingsFor = null; app.settingsSnapshot = null; app.settingsDirty = false; await pollState(); toast('Server settings saved.'); } catch (error) { toast(error.message, true); } finally { app.busy.delete(id); renderServer(); } });
$('confirm-form').addEventListener('submit', async event => { event.preventDefault(); if (!app.confirm) return; $('confirm-submit').disabled = true; setNotice('confirm-error', ''); try { await app.confirm(); } catch (error) { setNotice('confirm-error', error.message); } finally { $('confirm-submit').disabled = false; } });
$('create-backup').addEventListener('click', () => { const id = app.selected; confirmAction('Create an island backup', 'The server will write a fresh snapshot if it is running. Backups are kept separately from automatic saves.', 'Create backup', async () => { await api(serverUrl('/backups', id), { method: 'POST', body: { name: $('backup-name').value.trim() || null } }); $('confirm-dialog').close(); toast('Island backup created.'); await pollSaves(); }, true); });
$('import-save').addEventListener('click', () => $('save-file').click());
$('save-file').addEventListener('change', event => { const file = event.target.files[0]; event.target.value = ''; if (!file) return; if (file.size > 256 * 1024 * 1024 || !file.size) return toast('Choose a non-empty save file smaller than 256 MB.', true); const id = app.selected; confirmAction('Import to your backup library?', `Add “${file.name}” (${bytes(file.size)}) to this server’s backup library?\n\nAfter import, choose Restore while the server is stopped to make this save active. Use a save compatible with this dedicated server and its installed game version.`, 'Import save', async () => { await api(serverUrl(`/saves/import?name=${encodeURIComponent(file.name)}`, id), { method: 'POST', headers: { 'Content-Type': 'application/octet-stream' }, body: file, timeout: 180000 }); $('confirm-dialog').close(); toast('Save imported to the backup library. Choose Restore to make it active.'); await pollSaves(); }); });
$('copy-address').addEventListener('click', async () => { const address = $('connection-address').value; try { await navigator.clipboard.writeText(address); toast('Server address copied.'); } catch { toast(`Server address: ${address}`); } });
$('log-search').addEventListener('input', () => renderLogs(true)); $('autoscroll').addEventListener('change', () => { if ($('autoscroll').checked) $('console-output').scrollTop = $('console-output').scrollHeight; });
document.addEventListener('change', async event => {
  if (event.target.name !== 'gameDirectory') return;
  const field = event.target, form = field.closest('form'), directory = field.value.trim(), select = form.querySelector('[name="map"]');
  try { const result = await api(`/api/maps?gameDirectory=${encodeURIComponent(directory)}`, { timeout: 12000 }); if (field.value.trim() !== directory) return; const previous = select.value; select.innerHTML = result.maps.map(map => `<option>${escapeHtml(map)}</option>`).join(''); if (result.maps.includes(previous)) select.value = previous; }
  catch (error) { toast(`Could not read maps from that installation. ${error.message}`, true); }
});
document.querySelector('.tabs').addEventListener('keydown', event => { const tabs = ['overview', 'console', 'backups', 'settings']; let index = tabs.indexOf(app.tab); if (event.key === 'ArrowRight') index = (index + 1) % tabs.length; else if (event.key === 'ArrowLeft') index = (index + tabs.length - 1) % tabs.length; else if (event.key === 'Home') index = 0; else if (event.key === 'End') index = tabs.length - 1; else return; event.preventDefault(); setTab(tabs[index], true); });
document.querySelectorAll('dialog').forEach(dialog => dialog.addEventListener('click', event => { if (event.target === dialog) { const bounds = dialog.getBoundingClientRect(); if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close(); } }));
pollState().then(() => { pollLogs(); pollSaves(); });
setInterval(() => { pollState(); pollLogs(); if (app.tab === 'backups') pollSaves(); }, 2000);
