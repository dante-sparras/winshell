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
  readonly property var shown: searching ? Launcher.asArray(searchResults) : page === "all" ? allApps : Launcher.homeEntries(apps, showRecommended ? recent : [], pinIds)
  readonly property int pinnedCount: searching || page === "all" ? 0 : Launcher.asArray(pinnedApps).length

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
    pinsKnown = true
    if (Array.isArray(pinData.pins))
      pinIds = pinData.pins.slice()
  }

  function ensureSeed() {
    // The pin file loads during completion. Seeding earlier would replace a file that is already there.
    if (!pinsReady || pinsPath === "" || pinsKnown || pinFile.loaded)
      return
    if (Launcher.visibleApps(apps).length === 0)
      return
    pinsKnown = true
    pinIds = Launcher.defaultPins(apps)
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
    var count = Launcher.asArray(shown).length
    if (count === 0)
      selected = 0
    else if (selected >= count)
      selected = count - 1
    else if (selected < 0)
      selected = 0
  }

  function moveSelection(key) {
    selected = Launcher.move(selected, key, Launcher.asArray(shown).length, pinnedCount, Launcher.columns)
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
    var entries = Launcher.asArray(shown)
    if (selected < 0 || selected >= entries.length)
      return null
    return entries[selected]
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
      visible: Launcher.asArray(root.pinnedApps).length === 0
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
          delegate: MouseArea {
            id: tile
            required property int index
            required property var modelData
            objectName: "pinTile"
            property string appId: modelData.id
            property string appName: modelData.name
            property int entryIndex: index
            readonly property bool chosen: root.selected === entryIndex
            width: Launcher.tileWidth
            height: Launcher.tileHeight
            hoverEnabled: true
            acceptedButtons: Qt.LeftButton | Qt.RightButton
            Accessible.role: Accessible.Button
            Accessible.name: modelData.name
            Accessible.description: "Pinned"
            onClicked: mouse => {
              root.selected = entryIndex
              if (mouse.button === Qt.RightButton)
                root.showMenu(modelData, tile)
              else
                root.activateSelection()
            }

            Rectangle {
              objectName: "pinFill"
              anchors.fill: parent
              radius: Theme.controlCornerRadius
              color: tile.pressed ? Theme.subtleFillColorTertiary : tile.chosen || tile.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
              Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
            }

            Image {
              visible: (modelData.icon || "") !== ""
              source: root.iconSource(modelData.icon)
              sourceSize: Qt.size(Launcher.pinnedIcon, Launcher.pinnedIcon)
              x: (Launcher.tileWidth - Launcher.pinnedIcon) / 2
              y: 8
              width: Launcher.pinnedIcon
              height: Launcher.pinnedIcon
            }

            Text {
              x: 4
              y: 44
              width: Launcher.tileWidth - 8
              text: modelData.name
              horizontalAlignment: Text.AlignHCenter
              elide: Text.ElideRight
              font: Theme.typography.caption
              color: Theme.textFillColorPrimary
            }
          }
        }
      }
    }

    Text {
      objectName: "recommendedHeader"
      visible: Launcher.asArray(root.recommendedApps).length > 0
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
      height: Launcher.asArray(root.recommendedApps).length * Launcher.recommendedRow
      interactive: false
      keyNavigationEnabled: false
      clip: true
      model: root.recommendedApps
      delegate: MouseArea {
        id: row
        required property int index
        required property var modelData
        objectName: "appRow"
        property string appId: modelData.id
        property string appName: modelData.name
        property int entryIndex: root.pinnedCount + index
        readonly property bool chosen: root.selected === entryIndex
        width: Launcher.contentWidth
        height: Launcher.recommendedRow
        hoverEnabled: true
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        Accessible.role: Accessible.Button
        Accessible.name: modelData.name
        Accessible.description: "Recommended"
        onClicked: mouse => {
          root.selected = entryIndex
          if (mouse.button === Qt.RightButton)
            root.showMenu(modelData, row)
          else
            root.activateSelection()
        }

        Rectangle {
          objectName: "rowFill"
          anchors.fill: parent
          radius: Theme.controlCornerRadius
          color: row.pressed ? Theme.subtleFillColorTertiary : row.chosen || row.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
          Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
        }

        Image {
          objectName: "rowIcon"
          visible: (modelData.icon || "") !== ""
          source: root.iconSource(modelData.icon)
          sourceSize: Qt.size(Launcher.rowIcon, Launcher.rowIcon)
          x: 8
          y: (Launcher.recommendedRow - Launcher.rowIcon) / 2
          width: Launcher.rowIcon
          height: Launcher.rowIcon
        }

        Text {
          x: 48
          width: Launcher.contentWidth - 60
          height: Launcher.recommendedRow
          verticalAlignment: Text.AlignVCenter
          text: modelData.name
          elide: Text.ElideRight
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }
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
      delegate: MouseArea {
        id: allRow
        required property int index
        required property var modelData
        objectName: "allRow"
        property string appId: modelData.id
        property string appName: modelData.name
        property int entryIndex: index
        readonly property bool chosen: root.selected === entryIndex
        width: Launcher.contentWidth
        height: Launcher.allRow
        hoverEnabled: true
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        Accessible.role: Accessible.Button
        Accessible.name: modelData.name
        Accessible.description: "All apps"
        onClicked: mouse => {
          root.selected = entryIndex
          if (mouse.button === Qt.RightButton)
            root.showMenu(modelData, allRow)
          else
            root.activateSelection()
        }

        Rectangle {
          anchors.fill: parent
          radius: Theme.controlCornerRadius
          color: allRow.pressed ? Theme.subtleFillColorTertiary : allRow.chosen || allRow.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
          Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
        }

        Image {
          visible: (modelData.icon || "") !== ""
          source: root.iconSource(modelData.icon)
          sourceSize: Qt.size(Launcher.allIcon, Launcher.allIcon)
          x: 8
          y: (Launcher.allRow - Launcher.allIcon) / 2
          width: Launcher.allIcon
          height: Launcher.allIcon
        }

        Text {
          x: 40
          width: Launcher.contentWidth - 52
          height: Launcher.allRow
          verticalAlignment: Text.AlignVCenter
          text: modelData.name
          elide: Text.ElideRight
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }
      }
    }
  }

  Text {
    objectName: "emptySearch"
    x: Launcher.margin
    y: Launcher.searchY + Launcher.searchHeight + 24
    width: Launcher.contentWidth
    visible: root.searching && Launcher.asArray(root.searchResults).length === 0
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
    visible: root.searching && Launcher.asArray(root.searchResults).length > 0
    keyNavigationEnabled: false
    clip: true
    model: root.searchResults
    delegate: MouseArea {
      id: hit
      required property int index
      required property var modelData
      objectName: "searchRow"
      property string appId: modelData.id
      property string appName: modelData.name
      property int entryIndex: index
      readonly property bool chosen: root.selected === entryIndex
      width: Launcher.contentWidth
      height: Launcher.recommendedRow
      hoverEnabled: true
      acceptedButtons: Qt.LeftButton | Qt.RightButton
      Accessible.role: Accessible.Button
      Accessible.name: modelData.name
      Accessible.description: "Search"
      onClicked: mouse => {
        root.selected = entryIndex
        if (mouse.button === Qt.RightButton)
          root.showMenu(modelData, hit)
        else
          root.activateSelection()
      }

      Rectangle {
        objectName: "searchFill"
        anchors.fill: parent
        radius: Theme.controlCornerRadius
        color: hit.pressed ? Theme.subtleFillColorTertiary : hit.chosen || hit.containsMouse ? Theme.subtleFillColorSecondary : "transparent"
        Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }
      }

      Image {
        visible: (modelData.icon || "") !== ""
        source: root.iconSource(modelData.icon)
        sourceSize: Qt.size(Launcher.rowIcon, Launcher.rowIcon)
        x: 8
        y: (Launcher.recommendedRow - Launcher.rowIcon) / 2
        width: Launcher.rowIcon
        height: Launcher.rowIcon
      }

      Text {
        x: 48
        width: Launcher.contentWidth - 60
        height: Launcher.recommendedRow
        verticalAlignment: Text.AlignVCenter
        text: modelData.name
        elide: Text.ElideRight
        font: Theme.typography.body
        color: Theme.textFillColorPrimary
      }
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
}
