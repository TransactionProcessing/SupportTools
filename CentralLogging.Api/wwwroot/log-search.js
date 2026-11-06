(() => {
  const api = {
    key: null,
    services: [],
    fields: [],
    uploading: false,
    async get(url) {
      const response = await fetch(url, { headers: { 'X-Log-Api-Key': api.key } });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail || `Request failed (${response.status}).`);
      }
      return response.json();
    },
    async upload(file) {
      const form = new FormData();
      form.append('file', file);
      const response = await fetch('/api/logs/upload', {
        method: 'POST',
        headers: { 'X-Log-Api-Key': api.key },
        body: form
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail || `Upload failed (${response.status}).`);
      }
      return response.json();
    }
  };

  const el = (id) => document.getElementById(id);
  const service = el('service');
  const filterList = el('filter-list');
  const results = el('results');
  const dialog = el('key-dialog');
  const filterRows = [];
  let selectedFieldData = [];

  function setConnected(connected) {
    const status = el('connection-status');
    status.classList.toggle('connected', connected);
    status.innerHTML = connected ? '<i></i> Connected' : '<i></i> Not connected';
    el('search').disabled = !connected;
    el('upload').disabled = !connected || !el('log-file').files?.length || api.uploading;
    service.disabled = !connected;
    el('add-filter').disabled = !connected || !service.value || selectedFieldData.length === 0;
  }

  function toUtcInputValue(date) {
    return date.toISOString().slice(0, 16);
  }

  function setDefaultTimeRange() {
    const now = new Date();
    el('to').value = toUtcInputValue(now);
    el('from').value = toUtcInputValue(new Date(now.getTime() - 24 * 60 * 60 * 1000));
  }

  async function connect(key) {
    const previousKey = api.key;
    api.key = key;
    try {
      const response = await api.get('/api/logs/services');
      api.services = response.services || [];
      service.replaceChildren(new Option('All services', ''));
      for (const name of api.services) service.add(new Option(name, name));
      setConnected(true);
      dialog.close();
      el('key-error').textContent = '';
      await loadServiceFields();
      el('search-message').textContent = api.services.length ? `${api.services.length} service${api.services.length === 1 ? '' : 's'} available` : 'No services found yet';
    } catch (error) {
      api.key = previousKey;
      setConnected(Boolean(api.key));
      el('key-error').textContent = error.message;
      if (!dialog.open) dialog.showModal();
      el('api-key').value = '';
      el('api-key').focus();
    }
  }

  async function loadServiceFields() {
    filterRows.length = 0;
    filterList.replaceChildren();
    selectedFieldData = [];
    if (!service.value) {
      el('service-hint').textContent = 'Select a service to browse its structured fields.';
      showFieldEmpty('Select a service to see its available fields.');
      setConnected(Boolean(api.key));
      return;
    }
    el('service-hint').textContent = 'Loading fields…';
    try {
      const data = await api.get(`/api/logs/fields?serviceName=${encodeURIComponent(service.value)}`);
      api.fields = data.fields || [];
      selectedFieldData = api.fields.filter(item => (item.types || []).some(type => ['string', 'number', 'boolean'].includes(type)));
      el('service-hint').textContent = `${api.fields.length} structured field${api.fields.length === 1 ? '' : 's'} discovered`;
      showFieldEmpty(selectedFieldData.length ? 'Add a filter to narrow the results by a structured field.' : 'No scalar structured fields are available for this service yet.');
    } catch (error) {
      el('service-hint').textContent = 'Could not load fields.';
      showFieldEmpty(error.message, true);
    }
    setConnected(Boolean(api.key));
  }

  function showFieldEmpty(message, isError = false) {
    const box = document.createElement('div');
    box.className = isError ? 'inline-error' : 'empty-inline';
    box.textContent = message;
    filterList.replaceChildren(box);
  }

  function addFilter() {
    if (!selectedFieldData.length) return;
    const row = document.createElement('div');
    row.className = 'filter-row';
    const field = document.createElement('select');
    field.className = 'filter-field';
    field.setAttribute('aria-label', 'Structured field');
    field.add(new Option('Select field', ''));
    selectedFieldData.forEach(item => field.add(new Option(item.name, item.name)));
    const type = document.createElement('select');
    type.className = 'filter-type';
    type.setAttribute('aria-label', 'Value type');
    const value = document.createElement('input');
    value.className = 'filter-value';
    value.setAttribute('aria-label', 'Exact value');
    value.disabled = true;
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'remove-filter';
    remove.setAttribute('aria-label', 'Remove filter');
    remove.textContent = '×';
    row.append(field, type, value, remove);
    filterList.append(row);
    const item = { row, field, type, value };
    filterRows.push(item);

    field.addEventListener('change', () => {
      type.replaceChildren();
      const data = selectedFieldData.find(candidate => candidate.name === field.value);
      const types = (data?.types || []).filter(candidate => ['string', 'number', 'boolean'].includes(candidate));
      types.forEach(candidate => type.add(new Option(candidate, candidate)));
      type.disabled = !types.length;
      value.disabled = !types.length;
      updateValueControl(item);
    });
    type.addEventListener('change', () => updateValueControl(item));
    remove.addEventListener('click', () => {
      const index = filterRows.indexOf(item);
      if (index >= 0) filterRows.splice(index, 1);
      row.remove();
      if (!filterRows.length) showFieldEmpty('Add a filter to narrow the results by a structured field.');
    });
    if (filterRows.length === 1) filterList.replaceChildren(row);
  }

  function updateValueControl(item) {
    const kind = item.type.value;
    item.value.type = 'text';
    item.value.inputMode = kind === 'number' ? 'decimal' : 'text';
    item.value.placeholder = kind === 'boolean' ? 'true or false' : kind === 'number' ? 'Exact JSON number' : 'Exact value';
    item.value.disabled = !kind;
  }

  function isoFromInput(value) {
    if (!value) return '';
    return new Date(`${value}:00Z`).toISOString();
  }

  function buildSearchUrl() {
    const query = new URLSearchParams();
    const from = isoFromInput(el('from').value);
    const to = isoFromInput(el('to').value);
    if (from) query.set('fromUtc', from);
    if (to) query.set('toUtc', to);
    if (service.value) query.set('serviceName', service.value);
    const correlationId = el('correlation').value.trim();
    const tenantId = el('tenant').value.trim();
    if (correlationId) query.set('correlationId', correlationId);
    if (tenantId) query.set('tenantId', tenantId);
    query.set('take', '100');
    for (const item of filterRows) {
      if (!item.field.value || !item.type.value || !item.value.value) throw new Error('Complete each structured field filter or remove it.');
      const filter = { field: item.field.value, type: item.type.value, value: item.value.value };
      if (filter.type === 'boolean' && !/^(true|false)$/i.test(filter.value)) throw new Error('Boolean values must be true or false.');
      query.append('filter', JSON.stringify(filter));
    }
    return `/api/logs?${query.toString()}`;
  }

  async function search() {
    el('search-message').textContent = '';
    el('search').disabled = true;
    el('search').textContent = 'Searching…';
    results.replaceChildren();
    try {
      const data = await api.get(buildSearchUrl());
      el('result-count').textContent = `${data.length} event${data.length === 1 ? '' : 's'} (maximum 100)`;
      if (!data.length) {
        results.append(makeState('No matching events', 'Try widening the time window or removing one of the filters.'));
      } else {
        data.forEach(log => results.append(renderEvent(log)));
      }
    } catch (error) {
      if (!api.key) return;
      el('result-count').textContent = 'Search failed';
      const message = document.createElement('div');
      message.className = 'inline-error';
      message.textContent = error.message;
      results.append(message);
    } finally {
      el('search').disabled = !api.key;
      el('search').innerHTML = '<span aria-hidden="true">⌕</span> Search logs';
    }
  }

  async function uploadLogs() {
    const file = el('log-file').files?.[0];
    if (!api.key || !file || api.uploading) return;

    api.uploading = true;
    el('log-file').disabled = true;
    el('upload-message').className = 'upload-message';
    el('upload-message').textContent = `Uploading ${file.name}…`;
    setConnected(true);
    try {
      const summary = await api.upload(file);
      el('upload-message').textContent = `Upload complete: ${summary.accepted} imported, ${summary.duplicates} duplicate, ${summary.invalidLines} invalid line${summary.invalidLines === 1 ? '' : 's'}.`;
      el('result-count').textContent = 'Search again to view database results';
      results.replaceChildren(makeState('Upload complete', 'Run a search to view matching events in the database. Results can include events from previous imports.'));
    } catch (error) {
      el('upload-message').className = 'upload-message error-text';
      el('upload-message').textContent = error.message;
    } finally {
      api.uploading = false;
      el('log-file').disabled = false;
      setConnected(Boolean(api.key));
    }
  }

  function makeState(title, description) {
    const state = document.createElement('div');
    state.className = 'empty-state';
    const heading = document.createElement('h3');
    heading.textContent = title;
    const text = document.createElement('p');
    text.textContent = description;
    state.append(heading, text);
    return state;
  }

  function renderEvent(log) {
    const card = document.createElement('article');
    card.className = 'event-card';
    const top = document.createElement('div');
    top.className = 'event-top';
    const severity = document.createElement('span');
    severity.className = `severity ${String(log.severity || '').toLowerCase()}`;
    severity.textContent = log.severity || 'Unknown';
    const serviceName = document.createElement('span');
    serviceName.className = 'event-service';
    serviceName.textContent = log.serviceName || 'Unknown service';
    const timestamp = document.createElement('time');
    timestamp.className = 'event-time';
    timestamp.dateTime = log.timestampUtc || '';
    timestamp.textContent = log.timestampUtc ? new Date(log.timestampUtc).toLocaleString(undefined, { timeZone: 'UTC', timeZoneName: 'short' }) : '';
    top.append(severity, serviceName, timestamp);
    const body = document.createElement('div');
    body.className = 'event-body';
    const message = document.createElement('p');
    message.className = 'event-message';
    message.textContent = log.message || '';
    body.append(message);
    const meta = document.createElement('div');
    meta.className = 'event-meta';
    if (log.correlationId) meta.append(makeChip(`Correlation: ${log.correlationId}`));
    if (log.tenantId) meta.append(makeChip(`Tenant: ${log.tenantId}`));
    if (log.merchantId) meta.append(makeChip(`Merchant: ${log.merchantId}`));
    if (meta.childElementCount) body.append(meta);
    const properties = document.createElement('div');
    properties.className = 'event-properties';
    const data = typeof log.propertiesJson === 'string' ? safeJson(log.propertiesJson) : log.propertiesJson;
    if (data && typeof data === 'object') {
      for (const [name, value] of Object.entries(data)) {
        const chip = document.createElement('span');
        chip.className = 'property-chip';
        const label = document.createElement('b');
        label.textContent = `${name}: `;
        chip.append(label, document.createTextNode(formatValue(value)));
        properties.append(chip);
      }
    }
    if (properties.childElementCount) body.append(properties);
    if (log.exception) {
      const details = document.createElement('details');
      const summary = document.createElement('summary');
      summary.className = 'meta-chip';
      summary.textContent = 'Exception details';
      const exception = document.createElement('pre');
      exception.className = 'event-message';
      exception.textContent = log.exception;
      details.append(summary, exception);
      body.append(details);
    }
    card.append(top, body);
    return card;
  }

  function makeChip(value) {
    const chip = document.createElement('span');
    chip.className = 'meta-chip';
    chip.textContent = value;
    return chip;
  }

  function safeJson(value) {
    try { return JSON.parse(value); } catch { return null; }
  }

  function formatValue(value) {
    return value !== null && typeof value === 'object' ? JSON.stringify(value) : String(value);
  }

  service.addEventListener('change', loadServiceFields);
  el('add-filter').addEventListener('click', addFilter);
  el('search').addEventListener('click', search);
  el('log-file').addEventListener('change', () => {
    el('upload-message').textContent = '';
    el('upload-message').className = 'upload-message';
    setConnected(Boolean(api.key));
  });
  el('upload').addEventListener('click', uploadLogs);
  el('change-key').addEventListener('click', () => {
    setConnected(false);
    dialog.showModal();
    el('api-key').focus();
  });
  el('cancel-key').addEventListener('click', () => {
    if (api.key) {
      dialog.close();
      setConnected(true);
    }
    else el('key-error').textContent = 'An API key is needed to connect.';
  });
  el('key-form').addEventListener('submit', event => {
    event.preventDefault();
    const key = el('api-key').value;
    el('api-key').value = '';
    connect(key);
  });

  setDefaultTimeRange();
  setConnected(false);
  dialog.showModal();
  el('api-key').focus();
})();
