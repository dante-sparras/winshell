import Winshell
import Winshell.Theme
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// A list of things in an order the user picks, one card each: move up, move down and remove. Items are
// { key, title } with a glyph or an icon. The list keeps the keyboard on the button that was used.
Column {
  id: root

  property var items: []
  property string removeLabel: "Remove"
  property string removeGlyph: ""
  // Says what is not there, when there is nothing.
  property string emptyText: ""

  signal moveRequested(string key, int offset)
  signal removeRequested(string key)

  spacing: 4

  function focusButton(key, button) {
    const row = children.find(c => c.objectName === "item:" + key)
    if (!row)
      return
    const target = row.button(button)
    if (target && target.enabled && target.visible)
      target.forceActiveFocus()
    else if (row.button("remove"))
      row.button("remove").forceActiveFocus()
  }

  Text {
    width: parent.width
    visible: (root.items ?? []).length === 0 && root.emptyText !== ""
    text: root.emptyText
    font: Theme.typography.body
    color: Theme.textFillColorSecondary
    wrapMode: Text.Wrap
  }

  Repeater {
    model: root.items

    Rectangle {
      id: row
      required property var modelData
      required property int index
      objectName: "item:" + modelData.key
      width: root.width
      height: 48
      radius: Theme.controlCornerRadius
      color: Theme.cardBackgroundFillColorDefault
      border.width: 1
      border.color: Theme.cardStrokeColorDefault
      Accessible.role: Accessible.ListItem
      Accessible.name: modelData.title
      Accessible.description: (index + 1) + " of " + root.items.length

      function button(name) {
        return name === "up" ? up : name === "down" ? down : remove
      }

      function use(name, action) {
        const key = modelData.key
        action()
        Qt.callLater(() => root.focusButton(key, name))
      }

      function reveal() {
        for (let p = root.parent; p; p = p.parent) {
          if (p.objectName === "settingsScroll") {
            p.reveal(row)
            return
          }
        }
      }

      RowLayout {
        x: 16
        width: parent.width - 24
        anchors.verticalCenter: parent.verticalCenter
        spacing: 16

        Text {
          Layout.preferredWidth: 20
          visible: (row.modelData.glyph ?? "") !== ""
          text: row.modelData.glyph ?? ""
          horizontalAlignment: Text.AlignHCenter
          font.family: Theme.iconFontFamily
          font.pixelSize: 20
          color: Theme.textFillColorPrimary
          Accessible.ignored: true
        }

        Image {
          Layout.preferredWidth: 20
          Layout.preferredHeight: 20
          visible: (row.modelData.icon ?? "") !== ""
          source: row.modelData.icon ? Shell.iconPath(row.modelData.icon) : ""
          sourceSize: Qt.size(20, 20)
          Accessible.ignored: true
        }

        Text {
          Layout.fillWidth: true
          text: row.modelData.title
          elide: Text.ElideRight
          font: Theme.typography.body
          color: Theme.textFillColorPrimary
        }

        Button {
          id: up
          objectName: "up"
          text: ""
          font.family: Theme.iconFontFamily
          flat: true
          implicitWidth: 36
          enabled: row.index > 0
          Accessible.name: "Move " + row.modelData.title + " up"
          ToolTip.visible: hovered
          ToolTip.text: "Move up"
          onClicked: row.use("up", () => root.moveRequested(row.modelData.key, -1))
          onActiveFocusChanged: if (activeFocus) row.reveal()
        }

        Button {
          id: down
          objectName: "down"
          text: ""
          font.family: Theme.iconFontFamily
          flat: true
          implicitWidth: 36
          enabled: row.index < root.items.length - 1
          Accessible.name: "Move " + row.modelData.title + " down"
          ToolTip.visible: hovered
          ToolTip.text: "Move down"
          onClicked: row.use("down", () => root.moveRequested(row.modelData.key, 1))
          onActiveFocusChanged: if (activeFocus) row.reveal()
        }

        Button {
          id: remove
          objectName: "remove"
          text: root.removeGlyph
          font.family: Theme.iconFontFamily
          flat: true
          implicitWidth: 36
          Accessible.name: root.removeLabel + " " + row.modelData.title
          ToolTip.visible: hovered
          ToolTip.text: root.removeLabel
          onClicked: {
            const at = row.index
            root.removeRequested(row.modelData.key)
            // The keyboard goes to the item that takes its place.
            Qt.callLater(() => {
              const next = root.items[Math.min(at, root.items.length - 1)]
              if (next)
                root.focusButton(next.key, "remove")
            })
          }
          onActiveFocusChanged: if (activeFocus) row.reveal()
        }
      }
    }
  }
}
