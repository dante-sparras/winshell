import Winshell.Theme
import QtQuick
import QtQuick.Controls

// A quick settings tile as Windows 11 draws it: a 96 by 48 button with a glyph, filled with the accent color while
// it is on, and its name under it. A tile with a page of its own shows a chevron.
Item {
  id: tile

  property string glyph
  property string label
  property string description
  property bool on
  // Whether the tile shows something that is on or off, so Narrator reads it as a toggle.
  property bool toggle
  property bool hasPage

  signal clicked()

  implicitWidth: 96
  implicitHeight: button.height + 6 + name.implicitHeight

  Button {
    id: button
    objectName: "tileButton"
    width: 96
    height: 48
    padding: 0
    text: tile.label
    highlighted: tile.on
    Accessible.checkable: tile.toggle
    Accessible.checked: tile.on
    Accessible.description: tile.description
    onClicked: tile.clicked()

    contentItem: Item {
      Text {
        anchors.centerIn: parent
        text: tile.glyph
        font.family: Theme.iconFontFamily
        font.pixelSize: 16
        color: tile.on ? Theme.textOnAccentFillColorPrimary : Theme.textFillColorPrimary
      }

      Text {
        visible: tile.hasPage
        anchors.right: parent.right
        anchors.rightMargin: 10
        anchors.verticalCenter: parent.verticalCenter
        text: ""
        font.family: Theme.iconFontFamily
        font.pixelSize: 12
        color: tile.on ? Theme.textOnAccentFillColorPrimary : Theme.textFillColorPrimary
      }
    }
  }

  Text {
    id: name
    anchors.top: button.bottom
    anchors.topMargin: 6
    width: parent.width
    horizontalAlignment: Text.AlignHCenter
    elide: Text.ElideRight
    text: tile.label
    font.pixelSize: 12
    color: Theme.textFillColorPrimary
    // The button already carries the name.
    Accessible.ignored: true
  }
}
