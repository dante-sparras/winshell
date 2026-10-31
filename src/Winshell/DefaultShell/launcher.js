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
