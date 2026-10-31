import Winshell
import Winshell.Io
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import "launcher.js" as Launcher

// The Start surface, without its window, so a test can parent it to a scene.
// Pins live in launcher.toml next to the shell. An empty pins array is a choice; a missing file is not.
Item {
  id: root

  signal launched(app)
  signal locationRequested(app)
  signal dismissed()

  property var apps: []
  property var recent: []
  // Recommended apps under the pins; off shows the pins alone.
  property bool showRecommended: true
  property var searchResults: []
  property var pinIds: []
  property string pinsPath: ""
  property string page: "home"
  property int selected: 0
  property var menuApp: null
  property var lastLaunched: null
  property var lastLocation: null
  property int dismissCount: 0
  // The app the arrow keys selected last, which Narrator hears while the keyboard stays in the search box.
  property string selectionName: ""
  property bool pinsKnown: false
  property bool pinsReady: false
  // A shortcut path is not an image file. Draw it only while Start is open, through the icon provider.
  property bool showIcons: false

  readonly property string query: searchField.text
  readonly property bool searching: query.length > 0
  readonly property var pinnedApps: Launcher.pinned(apps, pinIds)
  readonly property var recommendedApps: showRecommended ? Launcher.recommended(recent, pinIds, Launcher.recommendedLimit) : []
  readonly property var allApps: Launcher.visibleApps(apps)
  readonly property var shown: searching ? searchResults : page === "all" ? allApps : Launcher.homeEntries(apps, showRecommended ? recent : [], pinIds)
  readonly property int pinnedCount: searching || page === "all" ? 0 : pinnedApps.length

  implicitWidth: Launcher.menuWidth
  implicitHeight: Launcher.menuHeight

  function iconSource(icon) {
    if (!showIcons || !icon)
      return ""
    return Shell.iconPath(icon)
  }

  function focusSearch() {
    searchField.forceActiveFocus()
  }

  function reset() {
    searchField.text = ""
    page = "home"
    selected = 0
  }

  function showAll() {
    page = "all"
    selected = 0
    searchField.forceActiveFocus()
  }

  function showHome() {
    page = "home"
    selected = 0
    searchField.forceActiveFocus()
  }

  function readPins() {
    // A file that does not parse is the same as a missing one: show the defaults, and do not write them over it.
    if (pinData.error) {
      pinsKnown = false
      return
    }
    pinsKnown = true
    if (Array.isArray(pinData.pins))
      pinIds = pinData.pins.slice()
  }

  function ensureSeed() {
    // The pin file loads during completion. Seeding earlier would replace a file that is already there.
    if (!pinsReady || pinsPath === "" || pinsKnown)
      return
    if (pinFile.loaded && !pinData.error)
      return
    if (Launcher.visibleApps(apps).length === 0)
      return
    pinsKnown = true
    pinIds = Launcher.defaultPins(apps)
    if (!pinData.error)
      writePins()
  }

  function writePins() {
    if (pinsPath === "")
      return
    pinData.pins = pinIds
    pinFile.writeAdapter()
  }

  function togglePin(app) {
    if (!app)
      return
    pinIds = Launcher.togglePin(pinIds, app.id)
    writePins()
    clampSelection()
  }

  function clampSelection() {
    selected = Math.max(0, Math.min(selected, shown.length - 1))
  }

  function moveSelection(key) {
    selected = Launcher.move(selected, key, shown.length, pinnedCount, Launcher.columns)
    reveal(selected)
    const app = currentApp()
    selectionName = app ? app.name : ""
  }

  function reveal(index) {
    if (searching) {
      resultsList.positionViewAtIndex(index, ListView.Contain)
      return
    }
    if (page === "all") {
      allList.positionViewAtIndex(index, ListView.Contain)
      return
    }
    if (index < pinnedCount) {
      var row = Math.floor(index / Launcher.columns)
      var y = row * (Launcher.tileHeight + Launcher.gap)
      if (y < pinScroll.contentY)
        pinScroll.contentY = y
      else if (y + Launcher.tileHeight > pinScroll.contentY + pinScroll.height)
        pinScroll.contentY = y + Launcher.tileHeight - pinScroll.height
    }
  }

  function currentApp() {
    return shown[selected] ?? null
  }

  function activateSelection() {
    var app = currentApp()
    if (!app)
      return
    lastLaunched = app
    launched(app)
  }

  function locationCommand(app) {
    return Launcher.locationCommand(app)
  }

  function openLocation(app) {
    if (!app)
      return
    lastLocation = Launcher.locationCommand(app)
    locationRequested(app)
  }

  function itemFor(index) {
    if (searching)
      return resultsList.itemAtIndex(index)
    if (page === "all")
      return allList.itemAtIndex(index)
    if (index < pinnedCount)
      return pinRepeater.itemAt(index)
    return recommendedList.itemAtIndex(index - pinnedCount)
  }

  function showMenu(app, item) {
    if (!app)
      return
    menuApp = app
    if (item)
      appMenu.popup(item, 0, item.height)
    else
      appMenu.popup()
  }

  function showMenuForSelection() {
    var app = currentApp()
    if (!app)
      return
    showMenu(app, itemFor(selected))
  }

  function escape() {
    if (query.length > 0) {
      searchField.text = ""
      selected = 0
      return
    }
    if (page === "all") {
      showHome()
      return
    }
    dismissCount = dismissCount + 1
    dismissed()
  }

  onAppsChanged: ensureSeed()
  onQueryChanged: selected = 0
  Component.onCompleted: {
    pinsReady = true
    ensureSeed()
    focusSearch()
  }

  FileView {
    id: pinFile
    path: root.pinsPath
    blockLoading: true
    blockWrites: true
    // Winshell Settings moves and unpins pins in the same file.
    watchChanges: true
    printErrors: false
    onFileChanged: reload()
    onLoaded: root.readPins()

    TomlAdapter {
      id: pinData
      comments: ({ pins: "The apps pinned to Start, in order. [] pins nothing; without this file Start pins its defaults." })
      // null until the file has pins.
      property var pins: null
    }
  }

  TextField {
    id: searchField
    objectName: "searchField"
    x: Launcher.searchX
    y: Launcher.searchY
    width: Launcher.searchWidth
    height: Launcher.searchHeight
    leftPadding: 36
    placeholderText: "Search apps"
    font: Theme.typography.body
    Accessible.name: "Search apps"
    Keys.priority: 0
    Keys.onUpPressed: root.moveSelection("up")
    Keys.onDownPressed: root.moveSelection("down")
    Keys.onLeftPressed: root.moveSelection("left")
    Keys.onRightPressed: root.moveSelection("right")
    Keys.onReturnPressed: root.activateSelection()
    Keys.onEnterPressed: root.activateSelection()
    Keys.onEscapePressed: root.escape()
    Keys.onPressed: event => {
      var menuKey = event.key === Qt.Key_Menu
      var shiftF10 = event.key === Qt.Key_F10 && (event.modifiers & Qt.ShiftModifier) !== 0
      if (menuKey || shiftF10)
        root.showMenuForSelection()
      else
        event.accepted = false
    }
  }

  Text {
    x: Launcher.searchX + 12
    y: Launcher.searchY
    width: 16
    height: Launcher.searchHeight
    verticalAlignment: Text.AlignVCenter
    text: "\uE721"
    font.family: Theme.iconFontFamily
    font.pixelSize: 16
    color: Theme.textFillColorSecondary
  }

  Column {
    id: homeColumn
    objectName: "homeColumn"
    x: Launcher.margin
    y: Launcher.searchY + Launcher.searchHeight + 16
    width: Launcher.contentWidth
    spacing: 8
    visible: !root.searching && root.page === "home"

    Text {
      objectName: "pinnedHeader"
      width: Launcher.contentWidth
      text: "Pinned"
      font: Theme.typography.bodyStrong
      color: Theme.textFillColorPrimary
      Accessible.role: Accessible.Heading
      Accessible.name: "Pinned"
    }

    Text {
      objectName: "emptyPins"
      visible: root.pinnedApps.length === 0
      width: Launcher.contentWidth
      text: "Nothing pinned"
      font: Theme.typography.caption
      color: Theme.textFillColorSecondary
    }

    Flickable {
      id: pinScroll
      objectName: "pinScroll"
      width: Launcher.contentWidth
      height: Launcher.pinnedViewport
      clip: true
      contentWidth: pinGrid.width
      contentHeight: pinGrid.height
      flickableDirection: Flickable.VerticalFlick

      Grid {
        id: pinGrid
        objectName: "pinGrid"
        columns: Launcher.columns
        columnSpacing: Launcher.gap
        rowSpacing: Launcher.gap

        Repeater {
          id: pinRepeater
          model: root.pinnedApps
          delegate: AppButton {
            objectName: "pinTile"
            width: Launcher.tileWidth
            height: Launcher.tileHeight
            tile: true
            iconSize: Launcher.pinnedIcon
            section: "Pinned"
          }
        }
      }
    }

    Text {
      objectName: "recommendedHeader"
      visible: root.recommendedApps.length > 0
      width: Launcher.contentWidth
      text: "Recommended"
      font: Theme.typography.bodyStrong
      color: Theme.textFillColorPrimary
      Accessible.role: Accessible.Heading
      Accessible.name: "Recommended"
    }

    ListView {
      id: recommendedList
      objectName: "recommendedList"
      width: Launcher.contentWidth
      height: root.recommendedApps.length * Launcher.recommendedRow
      interactive: false
      keyNavigationEnabled: false
      clip: true
      model: root.recommendedApps
      delegate: AppButton {
        objectName: "appRow"
        entryIndex: root.pinnedCount + index
        width: Launcher.contentWidth
        height: Launcher.recommendedRow
        section: "Recommended"
      }
    }

    MouseArea {
      id: allAppsButton
      objectName: "allAppsButton"
      width: Launcher.contentWidth
      height: 40
      hoverEnabled: true
      activeFocusOnTab: true
      Accessible.role: Accessible.Button
      Accessible.name: "All apps"
      Accessible.onPressAction: root.showAll()
      onClicked: root.showAll()
      Keys.onReturnPressed: root.showAll()
      Keys.onEnterPressed: root.showAll()
      Keys.onSpacePressed: root.showAll()
      Keys.onEscapePressed: root.escape()

      FocusRing { visible: allAppsButton.activeFocus }

      Rectangle {
        anchors.fill: parent
        radius: Theme.controlCornerRadius
        color: parent.pressed ? Theme.subtleFillColorTertiary : parent.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
        Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
      }

      Text {
        anchors.fill: parent
        leftPadding: 12
        verticalAlignment: Text.AlignVCenter
        text: "All apps"
        font: Theme.typography.body
        color: Theme.textFillColorPrimary
      }
    }
  }

  Column {
    objectName: "allColumn"
    x: Launcher.margin
    y: Launcher.searchY + Launcher.searchHeight + 16
    width: Launcher.contentWidth
    spacing: 8
    visible: !root.searching && root.page === "all"

    Row {
      spacing: 8
      width: Launcher.contentWidth

      MouseArea {
        id: backButton
        objectName: "allBack"
        width: 32
        height: 32
        hoverEnabled: true
        activeFocusOnTab: true
        Accessible.role: Accessible.Button
        Accessible.name: "Back"
        Accessible.onPressAction: root.showHome()
        onClicked: root.showHome()
        Keys.onReturnPressed: root.showHome()
        Keys.onEnterPressed: root.showHome()
        Keys.onSpacePressed: root.showHome()
        Keys.onEscapePressed: root.escape()

        FocusRing { visible: backButton.activeFocus }

        Rectangle {
          anchors.fill: parent
          radius: Theme.controlCornerRadius
          color: backButton.pressed ? Theme.subtleFillColorTertiary : backButton.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
          Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
        }

        Text {
          anchors.centerIn: parent
          text: "\uE72B"
          font.family: Theme.iconFontFamily
          font.pixelSize: 12
          color: Theme.textFillColorPrimary
        }
      }

      Text {
        objectName: "allHeader"
        width: Launcher.contentWidth - 40
        height: 32
        verticalAlignment: Text.AlignVCenter
        text: "All apps"
        font: Theme.typography.bodyStrong
        color: Theme.textFillColorPrimary
        Accessible.role: Accessible.Heading
        Accessible.name: "All apps"
      }
    }

    ListView {
      id: allList
      objectName: "allList"
      width: Launcher.contentWidth
      height: 480
      keyNavigationEnabled: false
      clip: true
      model: root.allApps
      delegate: AppButton {
        objectName: "allRow"
        width: Launcher.contentWidth
        height: Launcher.allRow
        iconSize: Launcher.allIcon
        section: "All apps"
      }
    }
  }

  Text {
    objectName: "emptySearch"
    x: Launcher.margin
    y: Launcher.searchY + Launcher.searchHeight + 24
    width: Launcher.contentWidth
    visible: root.searching && root.searchResults.length === 0
    text: "No results"
    font: Theme.typography.body
    color: Theme.textFillColorSecondary
  }

  ListView {
    id: resultsList
    objectName: "resultsList"
    x: Launcher.margin
    y: Launcher.searchY + Launcher.searchHeight + 12
    width: Launcher.contentWidth
    height: root.height - y - 16
    visible: root.searching && root.searchResults.length > 0
    keyNavigationEnabled: false
    clip: true
    model: root.searchResults
    delegate: AppButton {
      objectName: "searchRow"
      width: Launcher.contentWidth
      height: Launcher.recommendedRow
      section: "Search"
    }
  }

  Menu {
    id: appMenu
    objectName: "appMenu"
    MenuItem {
      id: pinItem
      objectName: "pinItem"
      text: root.menuApp && Launcher.isPinned(root.pinIds, root.menuApp.id) ? "Unpin from Start" : "Pin to Start"
      onTriggered: root.togglePin(root.menuApp)
    }
    MenuItem {
      id: locationItem
      objectName: "locationItem"
      text: "Open file location"
      onTriggered: root.openLocation(root.menuApp)
    }
  }

  Item {
    objectName: "selectionAnnouncement"
    Accessible.role: Accessible.Notification
    Accessible.name: root.selectionName
  }

  // The focus rectangle Windows draws around a focused button.
  component FocusRing: Rectangle {
    anchors.fill: parent
    anchors.margins: -3
    radius: Theme.controlCornerRadius + 3
    color: "transparent"
    border.width: 2
    border.color: Theme.focusStrokeColorOuter
  }

  // An app in Start: a pinned tile, or a row with its icon before its name.
  component AppButton: MouseArea {
    id: entry
    required property int index
    required property var modelData
    property int entryIndex: index
    property bool tile: false
    property int iconSize: Launcher.rowIcon
    // Where the app is shown, for assistive technology.
    property string section
    readonly property string appId: modelData.id
    readonly property string appName: modelData.name
    readonly property bool chosen: root.selected === entryIndex
    hoverEnabled: true
    acceptedButtons: Qt.LeftButton | Qt.RightButton
    Accessible.role: Accessible.Button
    Accessible.name: modelData.name
    Accessible.description: section
    onClicked: mouse => {
      root.selected = entryIndex
      if (mouse.button === Qt.RightButton)
        root.showMenu(modelData, entry)
      else
        root.activateSelection()
    }

    Rectangle {
      objectName: entry.tile ? "pinFill" : "rowFill"
      anchors.fill: parent
      radius: Theme.controlCornerRadius
      color: entry.pressed ? Theme.subtleFillColorTertiary : entry.chosen || entry.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
      Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
    }

    Image {
      objectName: entry.tile ? "pinIcon" : "rowIcon"
      visible: (entry.modelData.icon || "") !== ""
      source: root.iconSource(entry.modelData.icon)
      sourceSize: Qt.size(entry.iconSize, entry.iconSize)
      x: entry.tile ? (entry.width - entry.iconSize) / 2 : 8
      y: entry.tile ? 8 : (entry.height - entry.iconSize) / 2
      width: entry.iconSize
      height: entry.iconSize
    }

    Text {
      x: entry.tile ? 4 : entry.iconSize + 16
      y: entry.tile ? 44 : 0
      width: entry.width - x - (entry.tile ? 4 : 12)
      height: entry.tile ? implicitHeight : entry.height
      verticalAlignment: Text.AlignVCenter
      horizontalAlignment: entry.tile ? Text.AlignHCenter : Text.AlignLeft
      text: entry.modelData.name
      elide: Text.ElideRight
      font: entry.tile ? Theme.typography.caption : Theme.typography.body
      color: Theme.textFillColorPrimary
    }
  }
}
