// SubScoutScript.js — with Jellyfin auth header support

function $(sel) { return document.querySelector(sel); }

function readJellyfinToken() {
  try {
    const raw = localStorage.getItem('jellyfin_credentials');
    if (!raw) return null;
    const creds = JSON.parse(raw);
    const s = (creds.Servers || [])[0] || {};
    return {
      token: s.AccessToken || null,
      deviceId: s.DeviceId || null,
      appName: "SubScout",
      appVersion: "1.0.0"
    };
  } catch { return null; }
}

function authHeaders(initHeaders = {}) {
  const h = new Headers(initHeaders);
  const info = readJellyfinToken();
  if (info?.token) {
    const parts = [
      `MediaBrowser Client="${info.appName}"`,
      `Device="Browser"`,
      `DeviceId="${info.deviceId || 'SubScout-Device'}"`,
      `Version="${info.appVersion}"`,
      `Token="${info.token}"`
    ];
    h.set("X-Emby-Authorization", parts.join(", "));
  }
  return h;
}

function withApiKey(url) {
  const info = readJellyfinToken();
  if (!info?.token) return url;
  const u = new URL(url, location.origin);
  if (!u.searchParams.has('api_key')) u.searchParams.set('api_key', info.token);
  return u.toString();
}

async function getJson(url) {
  const res = await fetch(withApiKey(url), {
    credentials: "include",
    headers: authHeaders()
  });
  if (!res.ok) throw new Error(`${url} -> ${res.status}`);
  return res.json();
}

async function postJson(url, body) {
  const res = await fetch(withApiKey(url), {
    method: "POST",
    headers: authHeaders({ "Content-Type": "application/json" }),
    credentials: "include",
    body: JSON.stringify(body)
  });
  return res;
}

// ---------- form helpers (unchanged in spirit) ----------
function linesFromText(id) {
  const el = $(id);
  if (!el) return [];
  return (el.value || "")
    .split(/\r?\n/)
    .map(s => s.trim())
    .filter(Boolean);
}
function setTextFromLines(id, arr) {
  const el = $(id);
  if (!el) return;
  el.value = (arr || []).join("\n");
}

async function loadConfig() {
  const cfg = await getJson("/SubScout/Configuration");

  setTextFromLines("#templates", cfg.Templates);
  setTextFromLines("#extensions", cfg.Extensions);
  setTextFromLines("#languageSynonyms", cfg.LanguageSynonyms);

  $("#allowDeepMatch").checked = !!cfg.AllowDeepMatch;
  $("#maxDepth").value = (cfg.MaxDepth ?? 0).toString();
  $("#useCultureLanguageMap").checked = !!cfg.UseCultureLanguageMap;

  $("#onlyPathContains").value = cfg.OnlyPathContains || "";
  $("#onlyNameContains").value = cfg.OnlyNameContains || "";

  $("#copyToMediaFolder").checked = !!cfg.CopyToMediaFolder;
  $("#moveInsteadOfCopy").checked = !!cfg.MoveInsteadOfCopy;
  $("#overwriteExisting").checked = !!cfg.OverwriteExisting;

  $("#destinationPattern").value = cfg.DestinationPattern || "%fn%.%l%.%fe%";

  ensureDefaults(); // <— add this
}

function collectConfig() {
  ensureDefaults(); // <— add this
  return {
    Templates: linesFromText("#templates"),
    Extensions: linesFromText("#extensions"),
    LanguageSynonyms: linesFromText("#languageSynonyms"),

    AllowDeepMatch: $("#allowDeepMatch").checked,
    MaxDepth: parseInt($("#maxDepth").value || "0", 10) || 0,
    UseCultureLanguageMap: $("#useCultureLanguageMap").checked,

    OnlyPathContains: $("#onlyPathContains").value || "",
    OnlyNameContains: $("#onlyNameContains").value || "",

    CopyToMediaFolder: $("#copyToMediaFolder").checked,
    MoveInsteadOfCopy: $("#moveInsteadOfCopy").checked,
    OverwriteExisting: $("#overwriteExisting").checked,

    DestinationPattern: $("#destinationPattern").value || "%fn%.%l%.%fe%"
  };
}

async function saveConfig() {
  const payload = collectConfig();
  const res = await postJson("/SubScout/Configuration", payload);
  if (!res.ok) {
    const t = await res.text().catch(() => "");
    throw new Error(`Save failed: HTTP ${res.status}. ${t}`);
  }
}

async function testConfig() {
  const payload = collectConfig();
  const res = await postJson("/SubScout/Test", payload);
  if (!res.ok) {
    const t = await res.text().catch(() => "");
    throw new Error(`Test failed: HTTP ${res.status}. ${t}`);
  }
  const data = await res.json();
  alert(`SubScout: test OK.\nVisited=${data.report.itemsVisited}\nCandidates=${data.report.subCandidates}\nMatches=${data.report.matches}`);
}

function ensureDefaults() {
  const ex = document.querySelector("#extensions");
  if (ex && !ex.value.trim()) {
    ex.value = [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx"].join("\n");
  }
  const tm = document.querySelector("#templates");
  if (tm && !tm.value.trim()) {
    tm.value = [
      "%fn%.%l%.%fe%",
      "%fn%_%l%.%fe%",
      "%fn%.%fe%",
      "Subs/%fn%.%l%.%fe%",
      "Subs/%fn%_%l%.%fe%",
      "Subs/%fn%.%fe%",
      "Subs/%fn%/%n%_%l%.%fe%",
      "Subs/%fn%/%any%.%fe%",
      "Subs/%any%/%any%.%fe%"
    ].join("\n");
  }
  const ls = document.querySelector("#languageSynonyms");
  if (ls && !ls.value.trim()) {
    ls.value = [
      "en|eng|english",
      "fr|fra|fre|french",
      "de|ger|deu|german",
      "es|spa|spanish",
      "it|ita|italian",
      "pt|por|portuguese",
      "sv|swe|swedish",
      "da|dan|dansk|danish",
      "nl|dut|nld|dutch",
      "pl|pol|polish",
      "ru|rus|russian",
      "zh|chi|zho|chinese|chs|cht",
      "ja|jpn|japanese",
      "ko|kor|korean"
    ].join("\n");
  }
}


function restoreDefaults() {
  const defaults = [
    "%fn%.%l%.%fe%",
    "%fn%_%l%.%fe%",
    "%fn%.%fe%",
    "Subs/%fn%.%l%.%fe%",
    "Subs/%fn%_%l%.%fe%",
    "Subs/%fn%.%fe%",
    "Subs/%fn%/%n%_%l%.%fe%",
    "Subs/%fn%/%any%.%fe%",
    "Subs/%any%/%any%.%fe%"
  ];
  setTextFromLines("#templates", defaults);
  if (!$("#extensions").value.trim()) {
    setTextFromLines("#extensions", [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx"]);
  }
  if (!$("#languageSynonyms").value.trim()) {
    setTextFromLines("#languageSynonyms", [
      "en|eng|english",
      "fr|fra|fre|french",
      "de|ger|deu|german",
      "es|spa|spanish",
      "it|ita|italian",
      "pt|por|portuguese",
      "sv|swe|swedish",
      "da|dan|dansk|danish",
      "nl|dut|nld|dutch",
      "pl|pol|polish",
      "ru|rus|russian",
      "zh|chi|zho|chinese|chs|cht",
      "ja|jpn|japanese",
      "ko|kor|korean"
    ]);
  }
}

async function wire() {
  await loadConfig();

  $("#btnSave")?.addEventListener("click", async () => {
    try {
      await saveConfig();
      alert("SubScout: configuration saved.");
    } catch (e) {
      console.error(e);
      alert("SubScout: failed to save configuration. See console.");
    }
  });

  $("#btnTest")?.addEventListener("click", async () => {
    try {
      await testConfig();
    } catch (e) {
      console.error(e);
      alert("SubScout: test failed. Check console/server logs.");
    }
  });

  $("#btnRestoreDefaults")?.addEventListener("click", () => {
    try { restoreDefaults(); } catch (e) { console.error(e); }
  });
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", () => { setTimeout(() => wire().catch(console.error), 0); });
} else {
  setTimeout(() => wire().catch(console.error), 0);
}
