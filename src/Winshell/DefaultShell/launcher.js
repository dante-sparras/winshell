.pragma library

// Windows 11 Start, in logical pixels. The runtime scales these by the screen's DPI.
var menuWidth = 640
var menuHeight = 720
var margin = 22
var contentWidth = 596
var searchX = 22
var searchY = 16
var searchWidth = 596
var searchHeight = 32
var columns = 6
var tileWidth = 96
var tileHeight = 76
var gap = 4
var pinnedViewport = 3 * 76 + 2 * 4
var recommendedRow = 48
var recommendedLimit = 6
var allRow = 40
var pinnedIcon = 32
var rowIcon = 32
var allIcon = 24

// One pin per row. An AppUserModelID matches before a display name, so a non-English Windows still pins
// these apps. Names stay as a fallback when the index has no id for them.
var preferred = [
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

function asArray(value) {
  if (value === null || value === undefined)
    return []
  if (Array.isArray(value))
    return value
  if (typeof value.length === "number") {
    var out = []
    for (var i = 0; i < value.length; i++)
      out.push(value[i])
    return out
  }
  return []
}

function sameId(a, b) {
  if (a === null || a === undefined || b === null || b === undefined)
    return false
  return String(a).toLowerCase() === String(b).toLowerCase()
}

function isPinned(ids, id) {
  var keys = asArray(ids)
  for (var i = 0; i < keys.length; i++) {
    if (sameId(keys[i], id))
      return true
  }
  return false
}

function visibleApps(apps) {
  var list = asArray(apps)
  var out = []
  for (var i = 0; i < list.length; i++) {
    if (list[i] && !list[i].hidden)
      out.push(list[i])
  }
  return out
}

function pinned(apps, ids) {
  var list = visibleApps(apps)
  var keys = asArray(ids)
  var out = []
  for (var i = 0; i < keys.length; i++) {
    for (var j = 0; j < list.length; j++) {
      if (sameId(list[j].id, keys[i])) {
        out.push(list[j])
        break
      }
    }
  }
  return out
}

function recommended(recent, ids, limit) {
  var list = asArray(recent)
  var cap = limit > 0 ? limit : recommendedLimit
  var out = []
  for (var i = 0; i < list.length && out.length < cap; i++) {
    if (!list[i] || list[i].hidden || isPinned(ids, list[i].id))
      continue
    out.push(list[i])
  }
  return out
}

function homeEntries(apps, recent, ids) {
  var pins = pinned(apps, ids)
  var rest = recommended(recent, ids, recommendedLimit)
  var out = []
  for (var i = 0; i < pins.length; i++)
    out.push(pins[i])
  for (var j = 0; j < rest.length; j++)
    out.push(rest[j])
  return out
}

function nameHit(app, names) {
  var name = app && app.name ? String(app.name).toLowerCase() : ""
  for (var i = 0; i < names.length; i++) {
    if (name === String(names[i]).toLowerCase())
      return true
  }
  return false
}

function idHit(app, ids) {
  var id = app && app.id ? String(app.id) : ""
  var appId = app && app.appId ? String(app.appId) : ""
  for (var i = 0; i < ids.length; i++) {
    if (sameId(id, ids[i]) || sameId(appId, ids[i]))
      return true
  }
  return false
}

function matchPreferred(list, slot) {
  var i
  for (i = 0; i < list.length; i++) {
    if (idHit(list[i], slot.ids))
      return list[i]
  }
  for (i = 0; i < list.length; i++) {
    if (nameHit(list[i], slot.names))
      return list[i]
  }
  return null
}

function defaultPins(apps) {
  var list = visibleApps(apps)
  var ids = []
  for (var p = 0; p < preferred.length && ids.length < 12; p++) {
    var match = matchPreferred(list, preferred[p])
    if (match && !isPinned(ids, match.id))
      ids.push(match.id)
  }
  if (ids.length === 0) {
    for (var j = 0; j < list.length && ids.length < 8; j++)
      ids.push(list[j].id)
  }
  return ids
}

function togglePin(ids, id) {
  var keys = asArray(ids)
  var out = []
  var found = false
  for (var i = 0; i < keys.length; i++) {
    if (sameId(keys[i], id))
      found = true
    else
      out.push(keys[i])
  }
  if (!found)
    out.push(id)
  return out
}

function startsWith(value, prefix) {
  var text = value ? String(value) : ""
  var head = String(prefix).toLowerCase()
  return text.length >= head.length && text.substring(0, head.length).toLowerCase() === head
}

function locationCommand(app) {
  var folder = ["explorer.exe", "shell:AppsFolder"]
  if (!app)
    return folder
  var icon = app.icon ? String(app.icon) : ""
  var target = app.target ? String(app.target) : ""
  if (app.packaged || startsWith(icon, "shell:AppsFolder") || startsWith(target, "shell:AppsFolder"))
    return folder
  var path = icon || target
  if (!path)
    return folder
  return ["explorer.exe", "/select," + path]
}

// Home is a grid of pins, then one column of recommended rows. Search and All apps are one column.
function move(index, key, count, pinnedCount, columns) {
  if (count <= 0)
    return 0
  if (index < 0)
    index = 0
  if (index >= count)
    index = count - 1
  var cols = columns > 0 ? columns : 6
  var inPins = pinnedCount > 0 && index < pinnedCount
  if (key === "left") {
    if (!inPins || index % cols === 0)
      return index
    return index - 1
  }
  if (key === "right") {
    if (!inPins || index % cols === cols - 1 || index + 1 >= pinnedCount)
      return index
    return index + 1
  }
  if (key === "down") {
    if (pinnedCount <= 0)
      return Math.min(count - 1, index + 1)
    if (inPins) {
      var next = index + cols
      if (next < pinnedCount)
        return next
      // The cell below is empty, but the next row still has pins: land on its first one.
      var nextRow = Math.floor(index / cols) * cols + cols
      if (nextRow < pinnedCount)
        return nextRow
      return pinnedCount < count ? pinnedCount : index
    }
    return Math.min(count - 1, index + 1)
  }
  if (key === "up") {
    if (pinnedCount > 0 && index === pinnedCount)
      return Math.floor((pinnedCount - 1) / cols) * cols
    if (pinnedCount <= 0)
      return Math.max(0, index - 1)
    if (inPins)
      return index - cols >= 0 ? index - cols : index
    return index - 1
  }
  return index
}
