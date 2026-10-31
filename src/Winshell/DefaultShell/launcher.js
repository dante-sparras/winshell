.pragma library

// Windows 11 Start, in logical pixels. The runtime scales these by the screen's DPI.
const menuWidth = 640
const menuHeight = 720
const margin = 22
const contentWidth = 596
const searchX = 22
const searchY = 16
const searchWidth = 596
const searchHeight = 32
const columns = 6
const tileWidth = 96
const tileHeight = 76
const gap = 4
const pinnedViewport = 3 * 76 + 2 * 4
const recommendedRow = 48
const recommendedLimit = 6
const allRow = 40
const pinnedIcon = 32
const rowIcon = 32
const allIcon = 24

// One pin per row. An AppUserModelID matches before a display name, so a non-English Windows still pins
// these apps. Names stay as a fallback when the index has no id for them.
const preferred = [
  { ids: ["MSEdge", "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"], names: ["Edge", "Microsoft Edge"] },
  { ids: ["Microsoft.Windows.Explorer"], names: ["File Explorer", "Windows Explorer", "Explorer"] },
  { ids: ["windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"], names: ["Settings"] },
  { ids: ["Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"], names: ["Notepad"] },
  { ids: ["Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"], names: ["Calculator"] },
  { ids: ["Microsoft.Paint_8wekyb3d8bbwe!App"], names: ["Paint"] },
  { ids: ["Microsoft.ScreenSketch_8wekyb3d8bbwe!App"], names: ["Snipping Tool"] },
  { ids: ["Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"], names: ["Terminal", "Windows Terminal"] },
  { ids: ["Microsoft.Windows.Photos_8wekyb3d8bbwe!App"], names: ["Photos", "Microsoft Photos"] },
  { ids: ["Microsoft.WindowsStore_8wekyb3d8bbwe!App"], names: ["Microsoft Store"] },
  { ids: ["Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"], names: ["Clock", "Alarms & Clock"] }
]

// App ids, and the names default pins fall back to, match without regard to case.
function sameId(a, b) {
  return a != null && b != null && String(a).toLowerCase() === String(b).toLowerCase()
}

function isPinned(ids, id) {
  return (ids || []).some(k => sameId(k, id))
}

function visibleApps(apps) {
  return (apps || []).filter(a => a && !a.hidden)
}

function pinned(apps, ids) {
  const list = visibleApps(apps)
  return (ids || []).map(id => list.find(a => sameId(a.id, id))).filter(a => a)
}

function recommended(recent, ids, limit) {
  return visibleApps(recent).filter(a => !isPinned(ids, a.id)).slice(0, limit > 0 ? limit : recommendedLimit)
}

function homeEntries(apps, recent, ids) {
  return [...pinned(apps, ids), ...recommended(recent, ids, recommendedLimit)]
}

function defaultPins(apps) {
  const list = visibleApps(apps)
  const ids = []
  for (const slot of preferred) {
    const match = list.find(a => slot.ids.some(id => sameId(a.id, id) || sameId(a.appId, id)))
      ?? list.find(a => slot.names.some(name => sameId(a.name, name)))
    if (match && !isPinned(ids, match.id))
      ids.push(match.id)
  }
  return ids.length > 0 ? ids : list.slice(0, 8).map(a => a.id)
}

function togglePin(ids, id) {
  const keys = ids || []
  return isPinned(keys, id) ? keys.filter(k => !sameId(k, id)) : [...keys, id]
}

// A packaged app has no file to show, so its location is the Apps folder.
function locationCommand(app) {
  const folder = ["explorer.exe", "shell:AppsFolder"]
  const inFolder = path => String(path || "").toLowerCase().startsWith("shell:appsfolder")
  const path = app ? String(app.icon || app.target || "") : ""
  if (!path || app.packaged || inFolder(app.icon) || inFolder(app.target))
    return folder
  return ["explorer.exe", "/select," + path]
}

// Home is a grid of pins, then one column of recommended rows. Search and All apps are one column.
function move(index, key, count, pinnedCount, columns) {
  if (count <= 0)
    return 0
  index = Math.min(Math.max(index, 0), count - 1)
  const cols = columns > 0 ? columns : 6
  const inPins = pinnedCount > 0 && index < pinnedCount
  if (key === "left")
    return inPins && index % cols !== 0 ? index - 1 : index
  if (key === "right")
    return inPins && index % cols !== cols - 1 && index + 1 < pinnedCount ? index + 1 : index
  if (key === "down") {
    if (!inPins)
      return Math.min(count - 1, index + 1)
    if (index + cols < pinnedCount)
      return index + cols
    // The cell below is empty, but the next row still has pins: land on its first one.
    const nextRow = Math.floor(index / cols) * cols + cols
    if (nextRow < pinnedCount)
      return nextRow
    return pinnedCount < count ? pinnedCount : index
  }
  if (key === "up") {
    if (pinnedCount > 0 && index === pinnedCount)
      return Math.floor((pinnedCount - 1) / cols) * cols
    if (inPins)
      return index - cols >= 0 ? index - cols : index
    return Math.max(0, index - 1)
  }
  return index
}

// Moves id to the index target has in keys. Nothing changes when id is target, or target is not in keys.
function moveTo(keys, id, target) {
  if (id === target)
    return null
  var at = -1
  var i
  for (i = 0; i < keys.length; i++) {
    if (keys[i] === target) {
      at = i
      break
    }
  }
  if (at < 0)
    return null
  var list = []
  for (i = 0; i < keys.length; i++) {
    if (keys[i] !== id)
      list.push(keys[i])
  }
  list.splice(at, 0, id)
  return list
}

// Moves id past the entry offset steps away in visible. Entries visible does not contain keep their place.
function reorder(keys, visible, id, offset) {
  var at = -1
  var i
  for (i = 0; i < visible.length; i++) {
    if (sameId(visible[i], id)) {
      at = i
      break
    }
  }
  var neighbour = at < 0 ? undefined : visible[at + offset]
  if (at < 0 || !neighbour)
    return null
  var list = []
  for (i = 0; i < keys.length; i++) {
    if (!sameId(keys[i], id))
      list.push(keys[i])
  }
  var to = -1
  for (i = 0; i < list.length; i++) {
    if (sameId(list[i], neighbour)) {
      to = i
      break
    }
  }
  list.splice(offset > 0 ? to + 1 : to, 0, id)
  return list
}

// Windows Settings pages this shell already opens, from QuickSettingsContent and settings.js.
// Start search does not add pages of its own, and it does not search the web.
var settingsPages = [
  { name: "Wi-Fi", uri: "ms-settings:network-wifi", keywords: "wifi wi-fi wireless network", glyph: "\uE701" },
  { name: "Bluetooth", uri: "ms-settings:bluetooth", keywords: "bluetooth devices", glyph: "\uE702" },
  { name: "Airplane mode", uri: "ms-settings:network-airplanemode", keywords: "airplane flight mode", glyph: "\uE709" },
  { name: "Energy saver", uri: "ms-settings:batterysaver", keywords: "battery saver power", glyph: "\uE863" },
  { name: "Night light", uri: "ms-settings:nightlight", keywords: "night light blue light", glyph: "\uF08C" },
  { name: "Accessibility", uri: "ms-settings:easeofaccess", keywords: "accessibility ease of access", glyph: "\uE776" },
  { name: "More volume settings", uri: "ms-settings:apps-volume", keywords: "volume sound mixer apps", glyph: "\uE767" },
  { name: "Colors in Windows", uri: "ms-settings:colors", keywords: "colors colour transparency windows personalization", glyph: "\uE771" },
  { name: "Other system tray icons", uri: "ms-settings:taskbar", keywords: "tray notification area hidden icons overflow", glyph: "\uE712" },
  { name: "Do not disturb", uri: "ms-settings:notifications", keywords: "dnd focus quiet hours silence", glyph: "\uE708" },
  { name: "Startup apps in Windows", uri: "ms-settings:startupapps", keywords: "startup apps sign-in", glyph: "\uE7E8" },
  { name: "All settings", uri: "ms-settings:", keywords: "settings windows", glyph: "\uE713" }
]

function wordsOf(query) {
  return String(query || "").toLowerCase().split(/\s+/).filter(function (word) { return word.length > 0 })
}

function containsAll(text, words) {
  var hay = String(text || "").toLowerCase()
  for (var i = 0; i < words.length; i++) {
    if (hay.indexOf(words[i]) < 0)
      return false
  }
  return true
}

// Every word must be in the page's name or keywords. A name that starts with the query comes first.
function searchSettings(query) {
  var words = wordsOf(query)
  if (words.length === 0)
    return []
  var q = words.join(" ")
  var hits = []
  for (var i = 0; i < settingsPages.length; i++) {
    var page = settingsPages[i]
    if (!containsAll(page.name + " " + page.keywords, words))
      continue
    var title = page.name.toLowerCase()
    hits.push({ page: page, rank: title.indexOf(q) === 0 ? 0 : title.indexOf(q) > 0 ? 1 : 2, order: i })
  }
  hits.sort(function (a, b) { return a.rank - b.rank || a.order - b.order })
  var out = []
  for (var h = 0; h < hits.length; h++) {
    var match = hits[h].page
    out.push({ kind: "settings", name: match.name, uri: match.uri, path: match.uri, glyph: match.glyph })
  }
  return out
}

function asArray(value) {
  return Array.isArray(value) ? value : []
}

// Recent documents the caller already has. The name is matched; the path is not, so a folder name is not a hit.
function searchFiles(files, query) {
  var words = wordsOf(query)
  if (words.length === 0)
    return []
  var list = asArray(files)
  var out = []
  for (var i = 0; i < list.length; i++) {
    var file = list[i]
    if (!file || !containsAll(file.name, words))
      continue
    var path = file.path ? String(file.path) : ""
    out.push({ kind: "file", name: String(file.name), uri: path, path: path, glyph: "\uE8A5" })
  }
  return out
}

// Apps stay the first group. Settings and files follow, and only while there is a query.
function resultEntries(apps, settings, files) {
  var out = []
  var lists = [apps, settings, files]
  for (var g = 0; g < lists.length; g++) {
    var list = asArray(lists[g])
    for (var i = 0; i < list.length; i++)
      out.push(list[i])
  }
  return out
}
