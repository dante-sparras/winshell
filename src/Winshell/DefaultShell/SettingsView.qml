import Winshell.Theme
import QtQuick
import QtQuick.Controls
import "settings.js" as Settings

// Winshell Settings, without its window, so a test can parent it to a scene. As Windows 11 Settings lays it out:
// a navigation pane with "Find a setting" on the left, and the page on the right under a breadcrumb header.
Item {
  id: root

  // The page shown, a key of Settings.pages, and a page under it, or "".
  property string page: "personalization"
  property string subpage: ""

  // What the pages read: the notification listener, the shell's Hotkey objects, the app index, and the files
  // Start and quick settings keep.
  property var notificationListener: null
  property var startHotkey: null
  property var settingsHotkey: null
  property var apps: []
  property string pinsPath: ""
  property string tilesPath: ""
  // App icons come through the icon provider; a window that is not shown draws none.
  property bool showIcons: true

  readonly property string query: search.text
  readonly property bool searching: query.trim().length > 0
  readonly property var results: Settings.search(query)
  readonly property var crumbs: {
    if (searching)
      return [{ key: "", title: "Search results" }]
    const list = [{ key: page, title: Settings.page(page).title }]
    const sub = Settings.subpage(subpage)
    if (sub)
      list.push({ key: sub.key, title: sub.title })
    return list
  }

  implicitWidth: Settings.windowWidth
  implicitHeight: Settings.windowHeight

  // Opens a page, or the page a setting is on with the keyboard on that setting. Anything else stays where it was.
  function open(key) {
    const setting = Settings.settings.find(s => s.key === key)
    if (setting)
      goTo(setting)
    else if (Settings.pages.some(p => p.key === key))
      show(key)
    else
      console.warn("Winshell Settings has no page or setting named \"" + key + "\"")
  }

  function show(key, sub) {
    search.text = ""
    page = key
    subpage = sub ?? ""
    scroll.contentItem.contentY = 0
  }

  function goTo(setting) {
    show(setting.page)
    Qt.callLater(() => {
      const card = find(pages, "setting:" + setting.key)
      if (card)
        card.focusSetting()
    })
  }

  function find(item, name) {
    if (item.objectName === name && item.visible)
      return item
    for (const child of item.children) {
      const found = find(child, name)
      if (found)
        return found
    }
    return null
  }

  function focusResult(index) {
    const result = results[index]
    const card = result ? find(resultList, "result:" + result.key) : null
    if (card)
      card.forceActiveFocus()
  }

  function focusSearch() {
    search.forceActiveFocus()
  }

  // The navigation pane.
  Item {
    id: pane
    width: Settings.paneWidth
    height: parent.height

    TextField {
      id: search
      objectName: "search"
      x: 16
      y: 8
      width: pane.width - 32
      rightPadding: 36
      placeholderText: "Find a setting"
      Accessible.name: "Find a setting"
      Accessible.description: root.searching ? root.results.length + (root.results.length === 1 ? " result" : " results") : ""
      onAccepted: if (root.results.length > 0) root.goTo(root.results[0])
      Keys.onDownPressed: if (root.searching) root.focusResult(0)
      Keys.onEscapePressed: text = ""

      Text {
        anchors.right: parent.right
        anchors.rightMargin: 12
        anchors.verticalCenter: parent.verticalCenter
        text: "\uE721"
        font.family: Theme.iconFontFamily
        font.pixelSize: 14
        color: Theme.textFillColorSecondary
        Accessible.ignored: true
      }
    }

    ListView {
      boundsBehavior: Flickable.StopAtBounds
      id: nav
      objectName: "navigation"
      x: 8
      y: search.y + search.height + 16
      width: pane.width - 16
      height: pane.height - y - 8
      spacing: 4
      clip: true
      model: Settings.pages
      // No page is selected while search results show, for Narrator as on screen.
      currentIndex: root.searching ? -1 : Math.max(0, Settings.pages.findIndex(p => p.key === root.page))
      Accessible.role: Accessible.List
      Accessible.name: "Settings"
      onCurrentIndexChanged: if (currentIndex >= 0 && Settings.pages[currentIndex].key !== root.page) root.show(Settings.pages[currentIndex].key)

      delegate: Rectangle {
        id: entry
        required property var modelData
        required property int index
        readonly property bool selected: !root.searching && root.page === modelData.key
        objectName: "nav:" + modelData.key
        // Tab comes to the page shown; the arrow keys move to the others and show them.
        activeFocusOnTab: true
        width: nav.width
        height: 36
        radius: Theme.controlCornerRadius
        color: mouse.pressed ? Theme.subtleFillColorTertiary
          : selected || mouse.containsMouse ? Theme.subtleFillColorSecondary
          : "transparent"
        Accessible.role: Accessible.ListItem
        Accessible.name: modelData.title
        Accessible.onPressAction: root.show(modelData.key)
        Keys.onReturnPressed: root.show(modelData.key)
        Keys.onEnterPressed: root.show(modelData.key)
        Keys.onSpacePressed: root.show(modelData.key)

        Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }

        // The accent pill Windows draws beside the selected page.
        Rectangle {
          objectName: "pill"
          visible: entry.selected
          x: 0
          width: 3
          height: 16
          radius: 1.5
          anchors.verticalCenter: parent.verticalCenter
          color: Theme.accentFillColorDefault
        }

        // The keyboard is on this page in the pane.
        Rectangle {
          anchors.fill: parent
          visible: entry.activeFocus
          radius: entry.radius
          color: "transparent"
          border.width: 2
          border.color: Theme.focusStrokeColorOuter
        }

        Text {
          x: 16
          anchors.verticalCenter: parent.verticalCenter
          text: entry.modelData.glyph
          font.family: Theme.iconFontFamily
          font.pixelSize: 16
          color: Theme.textFillColorPrimary
          Accessible.ignored: true
        }

        Text {
          x: 48
          width: parent.width - 56
          anchors.verticalCenter: parent.verticalCenter
          text: entry.modelData.title
          elide: Text.ElideRight
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }

        MouseArea {
          id: mouse
          anchors.fill: parent
          hoverEnabled: true
          onClicked: root.show(entry.modelData.key)
        }
      }
    }
  }

  // The page.
  Item {
    id: content
    x: pane.width
    width: parent.width - pane.width
    height: parent.height

    // The breadcrumb: the page, then the page under it. An earlier part goes back to its page.
    Row {
      id: header
      objectName: "breadcrumb"
      x: 40
      y: 8
      width: content.width - 80
      height: 52
      spacing: 4
      Accessible.role: Accessible.Grouping
      Accessible.name: root.crumbs.map(c => c.title).join(", ")

      Repeater {
        model: root.crumbs

        Row {
          id: crumb
          required property var modelData
          required property int index
          readonly property bool last: index === root.crumbs.length - 1
          anchors.verticalCenter: parent.verticalCenter
          spacing: 4

          Text {
            visible: crumb.index > 0
            anchors.verticalCenter: parent.verticalCenter
            text: "\uE76C"
            font.family: Theme.iconFontFamily
            font.pixelSize: 16
            color: Theme.textFillColorSecondary
            leftPadding: 8
            rightPadding: 8
            Accessible.ignored: true
          }

          Text {
            objectName: "crumb:" + crumb.modelData.key
            anchors.verticalCenter: parent.verticalCenter
            text: crumb.modelData.title
            font: Theme.typography.title
            color: crumb.last ? Theme.textFillColorPrimary : crumbMouse.containsMouse ? Theme.textFillColorTertiary : Theme.textFillColorSecondary
            activeFocusOnTab: !crumb.last
            Accessible.role: crumb.last ? Accessible.Heading : Accessible.Link
            Accessible.name: crumb.modelData.title
            Accessible.onPressAction: if (!crumb.last) root.show(crumb.modelData.key)
            Keys.onReturnPressed: if (!crumb.last) root.show(crumb.modelData.key)
            Keys.onEnterPressed: if (!crumb.last) root.show(crumb.modelData.key)
            Keys.onSpacePressed: if (!crumb.last) root.show(crumb.modelData.key)

            Rectangle {
              objectName: "focusRing"
              anchors.fill: parent
              anchors.margins: -3
              visible: parent.activeFocus
              radius: Theme.controlCornerRadius
              color: "transparent"
              border.width: 2
              border.color: Theme.focusStrokeColorOuter
              Accessible.ignored: true
            }

            MouseArea {
              id: crumbMouse
              anchors.fill: parent
              enabled: !crumb.last
              hoverEnabled: true
              onClicked: root.show(crumb.modelData.key)
            }
          }
        }
      }
    }

    FocusScrollView {
      id: scroll
      objectName: "settingsScroll"
      y: header.y + header.height + 12
      width: content.width
      height: content.height - y
      contentWidth: availableWidth
      clip: true

      Item {
        width: scroll.availableWidth
        implicitHeight: pages.implicitHeight + 40

        Column {
          id: pages
          x: 40
          width: Math.min(Settings.contentMaxWidth, parent.width - 80)

          // The file is open in an editor and does not parse. The choices on the page stay as they were.
          Rectangle {
            objectName: "preferencesError"
            visible: Preferences.error !== ""
            width: parent.width
            implicitHeight: 64
            height: visible ? implicitHeight : 0
            radius: Theme.controlCornerRadius
            color: Theme.cardBackgroundFillColorDefault
            border.width: visible ? 1 : 0
            border.color: Theme.systemFillColorCaution

            Text {
              x: 16
              anchors.verticalCenter: parent.verticalCenter
              width: parent.width - 32
              wrapMode: Text.Wrap
              text: "Your preferences.toml has an error. Changes won't be saved until it's fixed."
              font: Theme.typography.body
              color: Theme.textFillColorPrimary
              Accessible.role: Accessible.StaticText
              Accessible.name: text
            }
          }

          Item {
            visible: Preferences.error !== ""
            width: 1
            height: visible ? 12 : 0
          }

          // What search found, one card each, that opens the setting.
          Column {
            id: resultList
            objectName: "results"
            visible: root.searching
            width: parent.width
            spacing: 4

            Text {
              width: parent.width
              visible: root.results.length === 0
              text: "No results for “" + root.query.trim() + "”"
              font: Theme.typography.body
              color: Theme.textFillColorSecondary
            }

            Repeater {
              model: root.results

              SettingsCard {
                id: result
                required property var modelData
                required property int index
                objectName: "result:" + modelData.key
                key: modelData.key
                description: Settings.page(modelData.page).title + (modelData.description !== "" ? " • " + modelData.description : "")
                // A result goes to its setting, even one that opens a page of Windows Settings.
                opensLink: false
                clickable: true
                Keys.onUpPressed: index > 0 ? root.focusResult(index - 1) : search.forceActiveFocus()
                Keys.onDownPressed: root.focusResult(index + 1)
                onClicked: root.goTo(modelData)
              }
            }
          }

          SettingsPersonalization {
            width: parent.width
            visible: !root.searching && root.page === "personalization"
          }

          SettingsTaskbar {
            width: parent.width
            visible: !root.searching && root.page === "taskbar"
          }

          SettingsStart {
            width: parent.width
            visible: !root.searching && root.page === "start" && root.subpage === ""
            onPinsRequested: root.show("start", "pins")
          }

          SettingsPins {
            width: parent.width
            visible: !root.searching && root.page === "start" && root.subpage === "pins"
            apps: root.apps
            pinsPath: root.pinsPath
            showIcons: root.showIcons
          }

          SettingsQuickSettings {
            width: parent.width
            visible: !root.searching && root.page === "quickSettings"
            layoutPath: root.tilesPath
          }

          SettingsNotifications {
            width: parent.width
            visible: !root.searching && root.page === "notifications"
            listener: root.notificationListener
          }

          SettingsHotkeys {
            width: parent.width
            visible: !root.searching && root.page === "hotkeys"
            startHotkey: root.startHotkey
            settingsHotkey: root.settingsHotkey
          }

          SettingsStartup {
            width: parent.width
            visible: !root.searching && root.page === "startup"
          }

          SettingsAbout {
            width: parent.width
            visible: !root.searching && root.page === "about"
          }
        }
      }
    }
  }
}
