.pragma library

// How the taskbar turns pinned apps and open windows into buttons, the way Windows 11 does: pinned apps first in
// the order they were pinned, then every other running app in the order it opened, each app's windows grouped on
// one button.

// The name of a program file without its folder or extension, in lower case: the app id Winshell gives the windows
// of a program that has no AppUserModelID.
function programId(path) {
  const name = String(path).split(/[\\/]/).pop()
  const dot = name.lastIndexOf(".")
  return (dot > 0 ? name.slice(0, dot) : name).toLowerCase()
}

// A pin is an app's id in the app index, or the path of a program the index does not have.
function isProgramPath(pin) {
  return /^[a-z]:[\\/]/i.test(pin) || pin.startsWith("\\\\")
}

// What a button is keyed by: the app index entry when there is one, else the app id its windows carry.
function pinKey(pin, find) {
  const entry = find(pin)
  if (entry)
    return "app:" + entry.id.toLowerCase()
  return "window:" + (isProgramPath(pin) ? programId(pin) : pin.toLowerCase())
}

function windowKey(window, find) {
  const entry = window.appId ? find(window.appId) : null
  if (entry)
    return "app:" + entry.id.toLowerCase()
  return "window:" + (window.appId || programId(window.executable)).toLowerCase()
}

// The AppUserModelID a jump list is read with, or "" when the task has none.
function appUserModelId(task) {
  if (task && task.entry && task.entry.appId)
    return task.entry.appId
  const windows = task && task.windows
  if (windows && windows.length > 0 && windows[0].appId)
    return windows[0].appId
  return ""
}

// The taskbar's buttons. `windows` is the window list in the order the windows opened, `pins` the pinned apps,
// `find` looks an app up in the index, and `shows` says whether this taskbar shows a window. Pinned apps are left
// out when `showPins` is false, unless one of their windows is shown.
function tasks(windows, pins, find, shows, showPins) {
  const byKey = {}
  const order = []
  function task(key) {
    if (!byKey[key]) {
      byKey[key] = { key: key, pin: "", entry: null, windows: [] }
      order.push(key)
    }
    return byKey[key]
  }
  for (const pin of pins || []) {
    const key = pinKey(pin, find)
    if (byKey[key])
      continue
    const t = task(key)
    t.pin = pin
    t.entry = find(pin)
  }
  for (const window of windows || []) {
    if (!shows(window))
      continue
    const t = task(windowKey(window, find))
    if (!t.entry && window.appId)
      t.entry = find(window.appId)
    t.windows.push(window)
  }
  // A pin the app index does not have yet, or no longer has, waits without a button: there is nothing to launch.
  return order.map(key => byKey[key])
    .filter(t => t.windows.length > 0 || (showPins && (t.entry || isProgramPath(t.pin))))
    .map(t => ({
      key: t.key,
      pin: t.pin,
      pinned: t.pin !== "",
      entry: t.entry,
      windows: t.windows,
      icon: t.entry ? t.entry.icon : t.windows.length > 0 ? t.windows[0].icon : t.pin,
      // The id to pin this app by: its entry in the app index, or the program it runs.
      pinId: t.pin || (t.entry ? t.entry.id : t.windows[0].executable),
    }))
}

// The app's name: its name in the app index, or the title of its one window, or its program's name. It reads the
// window titles, so call it from a binding of its own rather than from tasks(), which a title would then rebuild.
function name(t) {
  if (t.entry)
    return t.entry.name
  if (t.windows.length === 1)
    return t.windows[0].title
  if (t.windows.length > 0)
    return programId(t.windows[0].executable || t.windows[0].appId)
  return programId(t.pin)
}

// What Narrator reads for a button, as it reads Explorer's.
function accessibleName(t) {
  const count = t.windows.length
  if (count === 0)
    return name(t) + " pinned"
  return name(t) + " - " + count + (count === 1 ? " running window" : " running windows")
}

// What a left click does: launch an app that is not running, minimize the only window when it is in front,
// otherwise bring a window forward. With several windows, a click when one is in front goes to the next one, and
// otherwise to the one used last.
function clickAction(t, lastUsed) {
  if (t.windows.length === 0)
    return { launch: true }
  const front = t.windows.findIndex(w => w.activated && !w.minimized)
  if (t.windows.length === 1)
    return front === 0 ? { minimize: t.windows[0] } : { activate: t.windows[0] }
  if (front >= 0)
    return { activate: t.windows[(front + 1) % t.windows.length] }
  return { activate: t.windows.indexOf(lastUsed) >= 0 ? lastUsed : t.windows[0] }
}
