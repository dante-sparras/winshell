import Winshell.Theme
import QtQuick

// The heading over a group of setting cards.
Text {
  width: parent ? parent.width : 0
  topPadding: 20
  bottomPadding: 4
  font: Theme.typography.bodyStrong
  color: Theme.textFillColorPrimary
  wrapMode: Text.Wrap
  Accessible.role: Accessible.Heading
  Accessible.name: text
}
