.pragma library

// Winshell Settings: its pages, and every setting on them. A card takes its title, description and glyph from
// here, so search finds each setting by the words the card shows, and by a few more.

// Windows 11 Settings, in logical pixels.
var paneWidth = 280
var contentMaxWidth = 1000
var windowWidth = 1000
var windowHeight = 700

var pages = [
  { key: "personalization", title: "Personalization", glyph: "\uE771" },
  { key: "taskbar", title: "Taskbar", glyph: "\uE7C4" },
  { key: "start", title: "Start", glyph: "\uE80F" },
  { key: "quickSettings", title: "Quick settings", glyph: "\uE9E9" },
  { key: "notifications", title: "Notifications", glyph: "\uEA8F" },
  { key: "hotkeys", title: "Hotkeys", glyph: "\uE765" },
  { key: "startup", title: "Startup", glyph: "\uE7E8" },
  { key: "about", title: "About", glyph: "\uE946" }
]

// Pages under a page, which the header shows as a breadcrumb.
var subpages = [
  { key: "pins", page: "start", title: "Pinned apps" }
]

var settings = [
  { key: "colorScheme", page: "personalization", glyph: "\uE706", title: "Choose your mode", description: "Light or dark for the taskbar, Start, flyouts and this window", keywords: "dark light theme color colour" },
  { key: "accentColor", page: "personalization", glyph: "\uE790", title: "Accent color", description: "The color of buttons, switches and highlights", keywords: "accent colour highlight theme" },
  { key: "material", page: "personalization", glyph: "\uE81E", title: "Material", description: "What shows through the taskbar and flyouts", keywords: "mica acrylic transparency backdrop blur" },
  { key: "windowsColors", page: "personalization", glyph: "\uE771", title: "Colors in Windows", description: "Mode, accent color and transparency effects for Windows itself", keywords: "transparency windows", uri: "ms-settings:colors" },

  { key: "taskbarAlignment", page: "taskbar", glyph: "\uE8E3", title: "Taskbar alignment", description: "Where Start and the apps sit", keywords: "center centre left middle" },
  { key: "taskbarAutoHide", page: "taskbar", glyph: "\uE890", title: "Automatically hide the taskbar", description: "It comes back when the pointer reaches the bottom of the screen", keywords: "auto-hide autohide" },
  { key: "taskbarAllScreens", page: "taskbar", glyph: "\uE7F4", title: "Show my taskbar on all displays", description: "Off shows it on the main display only", keywords: "monitor screen multiple displays" },
  { key: "taskbarAppsOn", page: "taskbar", glyph: "\uE7F4", title: "When using multiple displays, show my taskbar apps on", description: "", keywords: "monitor screen multiple displays apps" },
  { key: "trayIcons", page: "taskbar", glyph: "\uE712", title: "Other system tray icons", description: "Windows chooses which icons show on the taskbar and which are hidden", keywords: "notification area tray hidden icons overflow", uri: "ms-settings:taskbar" },

  { key: "startRecommended", page: "start", glyph: "\uE81C", title: "Show recommended apps", description: "Apps you used lately, under your pins", keywords: "recent recently used" },
  { key: "startPins", page: "start", glyph: "\uE718", title: "Pinned apps", description: "Change their order, or unpin them", keywords: "pins pin unpin order", subpage: "pins" },

  { key: "quickSettingsTiles", page: "quickSettings", glyph: "\uE9E9", title: "Tiles", description: "Which tiles quick settings shows, in order. A tile this PC has no hardware for stays hidden.", keywords: "wifi bluetooth airplane energy saver night light accessibility tiles order" },

  { key: "doNotDisturb", page: "notifications", glyph: "\uE708", title: "Do not disturb", description: "Windows turns it on and off", keywords: "dnd focus quiet hours silence", uri: "ms-settings:notifications" },
  { key: "notificationApps", page: "notifications", glyph: "\uEA8F", title: "Notifications from apps", description: "Turn an app off to leave its notifications out of the notification center. Windows still decides whether it shows banners.", keywords: "per app apps senders notification center" },

  { key: "hotkeyStart", page: "hotkeys", glyph: "\uE80F", title: "Open Start", description: "", keywords: "keyboard shortcut windows key win" },
  { key: "hotkeySettings", page: "hotkeys", glyph: "\uE713", title: "Open Winshell Settings", description: "", keywords: "keyboard shortcut" },

  { key: "runAtSignIn", page: "startup", glyph: "\uE7E8", title: "Run Winshell when I sign in", description: "For your account only", keywords: "startup start automatically login logon boot" },
  { key: "startupApps", page: "startup", glyph: "\uE7E8", title: "Startup apps in Windows", description: "Windows can turn off an app that runs at sign-in", keywords: "startup apps", uri: "ms-settings:startupapps" },

  { key: "version", page: "about", glyph: "\uE946", title: "Version", description: "", keywords: "about release" },
  { key: "winshellUpdates", page: "about", glyph: "\uE895", title: "Winshell updates", description: "", keywords: "update upgrade new version download restart check" },
  { key: "autoUpdate", page: "about", glyph: "\uE896", title: "Get Winshell updates automatically", description: "Download a new version in the background. It installs when Winshell next starts.", keywords: "update upgrade automatic download" },
  { key: "configFolder", page: "about", glyph: "\uE8B7", title: "Config folder", description: "", keywords: "configuration folder appdata files preferences" },
  { key: "log", page: "about", glyph: "\uE9F9", title: "Log", description: "What this instance of Winshell reported", keywords: "logs log file errors debug" },
  { key: "diagnostics", page: "about", glyph: "\uE9D9", title: "Diagnostics", description: "Check how this PC is set up for Winshell, as winshell doctor does", keywords: "doctor troubleshoot problems check setup crash reports" }
]

// The quick settings tiles, as QuickSettingsContent names them, in its default order.
var tiles = [
  { key: "wifi", title: "Wi-Fi", glyph: "\uE701" },
  { key: "bluetooth", title: "Bluetooth", glyph: "\uE702" },
  { key: "airplaneMode", title: "Airplane mode", glyph: "\uE709" },
  { key: "energySaver", title: "Energy saver", glyph: "\uE863" },
  { key: "powerMode", title: "Power mode", glyph: "\uEC4A" },
  { key: "nightLight", title: "Night light", glyph: "\uF08C" },
  { key: "accessibility", title: "Accessibility", glyph: "\uE776" }
]

// The Windows accent colors, as Settings > Personalization > Colors lays them out, with the names Narrator reads.
var accents = [
  ["#FFB900", "Yellow gold"], ["#FF8C00", "Gold"], ["#F7630C", "Orange bright"], ["#CA5010", "Orange dark"], ["#DA3B01", "Rust"], ["#EF6950", "Pale rust"], ["#D13438", "Brick red"], ["#FF4343", "Mod red"],
  ["#E74856", "Pale red"], ["#E81123", "Red"], ["#EA005E", "Rose bright"], ["#C30052", "Rose"], ["#E3008C", "Plum light"], ["#BF0077", "Plum"], ["#C239B3", "Orchid light"], ["#9A0089", "Orchid"],
  ["#0078D4", "Default blue"], ["#0063B1", "Navy blue"], ["#8E8CD8", "Purple shadow"], ["#6B69D6", "Purple shadow dark"], ["#8764B8", "Iris pastel"], ["#744DA9", "Iris spring"], ["#B146C2", "Violet red light"], ["#881798", "Violet red"],
  ["#0099BC", "Cool blue bright"], ["#2D7D9A", "Cool blue"], ["#00B7C3", "Seafoam"], ["#038387", "Seafoam teal"], ["#00B294", "Mint light"], ["#018574", "Mint dark"], ["#00CC6A", "Turf green"], ["#10893E", "Sport green"],
  ["#7A7574", "Gray"], ["#5D5A58", "Gray brown"], ["#68768A", "Steel blue"], ["#515C6B", "Metal blue"], ["#567C73", "Pale moss"], ["#486860", "Moss"], ["#498205", "Meadow green"], ["#107C10", "Green"],
  ["#767676", "Overcast"], ["#4C4A48", "Storm"], ["#69797E", "Blue gray"], ["#4A5459", "Gray dark"], ["#647C64", "Liddy green"], ["#525E54", "Sage"], ["#847545", "Camouflage desert"], ["#7E735F", "Camouflage"]
]

function page(key) {
  for (var i = 0; i < pages.length; i++) {
    if (pages[i].key === key)
      return pages[i]
  }
  return pages[0]
}

function subpage(key) {
  for (var i = 0; i < subpages.length; i++) {
    if (subpages[i].key === key)
      return subpages[i]
  }
  return null
}

function setting(key) {
  for (var i = 0; i < settings.length; i++) {
    if (settings[i].key === key)
      return settings[i]
  }
  return { key: key, page: "", glyph: "", title: key, description: "", keywords: "" }
}

function accentName(color) {
  var c = String(color).toUpperCase()
  for (var i = 0; i < accents.length; i++) {
    if (accents[i][0] === c)
      return accents[i][1]
  }
  return c
}

// Every word of the query must be in the setting's title, description, keywords or page; settings whose title
// starts with the query come first.
function search(query) {
  var words = String(query || "").toLowerCase().split(/\s+/).filter(function (w) { return w.length > 0 })
  if (words.length === 0)
    return []
  var q = words.join(" ")
  var hits = []
  for (var i = 0; i < settings.length; i++) {
    var s = settings[i]
    var text = (s.title + " " + s.description + " " + s.keywords + " " + page(s.page).title).toLowerCase()
    var all = true
    for (var w = 0; w < words.length; w++) {
      if (text.indexOf(words[w]) < 0) {
        all = false
        break
      }
    }
    if (all)
      hits.push({ setting: s, rank: s.title.toLowerCase().indexOf(q) === 0 ? 0 : s.title.toLowerCase().indexOf(q) > 0 ? 1 : 2, order: i })
  }
  hits.sort(function (a, b) { return a.rank - b.rank || a.order - b.order })
  return hits.map(function (h) { return h.setting })
}

// Moves the item at index by offset in a copy of the list.
function moved(list, index, offset) {
  var out = list.slice()
  var to = index + offset
  if (index < 0 || index >= out.length || to < 0 || to >= out.length)
    return out
  var item = out.splice(index, 1)[0]
  out.splice(to, 0, item)
  return out
}
