const $ = (selector) => document.querySelector(selector);
const folders = $("#folders");
let revision = "missing";
let configuration = null;

const request = async (url, options = {}) => {
  const response = await fetch(url, options);
  if (response.status === 401) {
    throw new Error("Anmeldung erforderlich. Seite neu laden und Benutzer admin verwenden.");
  }
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const message = body?.errors?.join("\n") || body?.error || `${response.status} ${response.statusText}`;
    throw new Error(message);
  }
  return body;
};

const mutate = (method, body) => ({
  method,
  headers: {
    "Content-Type": "application/json",
    "X-PicCompressor-Request": "1"
  },
  body: body === undefined ? undefined : JSON.stringify(body)
});

function addFolder(value = {}) {
  const node = $("#folder-template").content.firstElementChild.cloneNode(true);
  node._configuration = value;
  node.querySelector('[data-field="name"]').value = value.name || "";
  node.querySelector('[data-field="input"]').value = value.input || "";
  node.querySelector('[data-field="output"]').value = value.output || "";
  node.querySelector('[data-field="recursive"]').checked = value.recursive !== false;
  const overwrite = node.querySelector('[data-field="overwriteOriginal"]');
  overwrite.checked = value.overwriteOriginal === true;
  overwrite.addEventListener("change", () => applyOverwrite(node));
  applyOverwrite(node);
  node.querySelector(".remove-folder").addEventListener("click", () => {
    node.remove();
    numberFolders();
  });
  folders.append(node);
  numberFolders();
}

// Beim Ersetzen der Originale ist die Eingabe zugleich das Ziel; ein Ausgabeordner entfällt.
function applyOverwrite(node) {
  const overwrite = node.querySelector('[data-field="overwriteOriginal"]').checked;
  const output = node.querySelector('[data-field="output"]');
  output.disabled = overwrite;
  output.required = !overwrite;
  node.querySelector(".overwrite-warning").hidden = !overwrite;
}

function applyKeepNames() {
  $("#suffix").disabled = $("#keep-names").checked;
}

function numberFolders() {
  [...folders.children].forEach((card, index) => {
    card.querySelector(".folder-number").textContent = String(index + 1).padStart(2, "0");
  });
}

function fill(document) {
  revision = document.revision;
  configuration = document.configuration;
  const defaults = configuration.defaults || {};
  $("#mode").value = configuration.mode || "Interval";
  $("#interval").value = configuration.intervalSeconds ?? 900;
  $("#stable").value = configuration.stableForSeconds ?? 120;
  $("#parallelism").value = configuration.parallelism ?? 1;
  $("#timeout").value = configuration.timeoutSeconds ?? 300;
  $("#quality").value = defaults.quality ?? 80;
  $("#quality-value").textContent = $("#quality").value;
  $("#chroma").value = String(defaults.chromaSubsampling || "420").replace("Subsampling", "");
  $("#exif").value = defaults.exif || "Remove";
  $("#savings").value = defaults.minSavingsPercent ?? 5;
  $("#larger-output").value = defaults.largerOutput || "Discard";
  // Ein leerer Zusatz übernimmt den Originalnamen in den Zielordner, wie in der Desktop-GUI.
  const suffix = defaults.suffix ?? "_compressed";
  $("#keep-names").checked = suffix === "";
  $("#suffix").value = suffix || "_compressed";
  applyKeepNames();
  folders.replaceChildren();
  (configuration.folders || []).forEach(addFolder);
  if (!folders.children.length) addFolder();
}

function collect() {
  const folderValues = [...folders.children].map((card) => {
    const overwrite = card.querySelector('[data-field="overwriteOriginal"]').checked;
    return {
      ...card._configuration,
      name: card.querySelector('[data-field="name"]').value.trim(),
      input: card.querySelector('[data-field="input"]').value.trim(),
      output: overwrite ? undefined : card.querySelector('[data-field="output"]').value.trim(),
      recursive: card.querySelector('[data-field="recursive"]').checked,
      overwriteOriginal: overwrite || undefined
    };
  });
  const defaults = configuration?.defaults || {};
  return {
    ...configuration,
    schemaVersion: 1,
    mode: $("#mode").value,
    intervalSeconds: Number($("#interval").value),
    debounceSeconds: configuration?.debounceSeconds ?? 10,
    stableForSeconds: Number($("#stable").value),
    parallelism: Number($("#parallelism").value),
    timeoutSeconds: Number($("#timeout").value),
    defaults: {
      ...defaults,
      quality: Number($("#quality").value),
      chromaSubsampling: $("#chroma").value,
      progressiveLevel: defaults.progressiveLevel ?? 2,
      exif: $("#exif").value,
      colorProfile: defaults.colorProfile || "Preserve",
      alphaBackground: defaults.alphaBackground || "#FFFFFF",
      collision: defaults.collision || "Skip",
      largerOutput: $("#larger-output").value,
      minSavingsPercent: Number($("#savings").value),
      suffix: $("#keep-names").checked ? "" : $("#suffix").value.trim() || "_compressed"
    },
    folders: folderValues
  };
}

function showMessage(text, error = false) {
  const message = $("#message");
  message.hidden = false;
  message.textContent = text;
  message.classList.toggle("error", error);
}

function formatTime(value) {
  return value ? new Intl.DateTimeFormat("de-DE", { dateStyle: "short", timeStyle: "medium" }).format(new Date(value)) : "—";
}

async function loadStatus() {
  try {
    const status = await request("/api/status");
    const running = status.state === "running";
    const error = status.state === "error" || status.state === "blocked";
    $("#status-dot").className = `status-dot ${running ? "running" : error ? "error" : "ok"}`;
    $("#status-title").textContent = running
      ? `Verarbeitet ${status.currentFolder || "Ordner"}`
      : error ? "Aufmerksamkeit erforderlich" : "Automatik aktiv";
    $("#status-detail").textContent = status.lastError
      || (running ? `Gestartet ${formatTime(status.startedAt)}` : `Nächster Lauf ${formatTime(status.nextRunAt)}`);
    // Die Zahlen gelten für einen Lauf; ein Folgelauf ohne neue Dateien zählt alles als erledigt.
    $("#metrics-caption").textContent = running
      ? "Aktueller Lauf"
      : `Letzter Lauf · ${formatTime(status.finishedAt)}`;
    $("#metric-success").textContent = status.succeeded ?? 0;
    $("#metric-unchanged").textContent = status.unchanged ?? 0;
    $("#metric-failed").textContent = status.failed ?? 0;
    $("#connection").textContent = running ? "Scan läuft" : "Verbunden";
  } catch (error) {
    $("#connection").textContent = "Nicht verbunden";
    $("#status-dot").className = "status-dot error";
    $("#status-title").textContent = "Verbindung fehlgeschlagen";
    $("#status-detail").textContent = error.message;
  }
}

$("#quality").addEventListener("input", (event) => {
  $("#quality-value").textContent = event.target.value;
});
$("#add-folder").addEventListener("click", () => addFolder());
$("#keep-names").addEventListener("change", applyKeepNames);
$("#scan-button").addEventListener("click", async () => {
  const button = $("#scan-button");
  button.disabled = true;
  try {
    await request("/api/scans", mutate("POST"));
    showMessage("Scan wurde eingeplant.");
    setTimeout(loadStatus, 350);
  } catch (error) {
    showMessage(error.message, true);
  } finally {
    button.disabled = false;
  }
});
$("#config-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = $("#save-button");
  button.disabled = true;
  try {
    const saved = await request("/api/config", mutate("PUT", { configuration: collect(), revision }));
    fill(saved);
    showMessage("Konfiguration gespeichert. Die neuen Einstellungen werden beim nächsten Lauf verwendet.");
    setTimeout(loadStatus, 350);
  } catch (error) {
    showMessage(error.message, true);
  } finally {
    button.disabled = false;
  }
});

(async () => {
  try {
    fill(await request("/api/config"));
    await loadStatus();
    setInterval(loadStatus, 3000);
  } catch (error) {
    showMessage(error.message, true);
  }
})();
