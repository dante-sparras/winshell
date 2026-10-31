import Winshell.Theme
import QtQuick
import "settings.js" as Settings

// One setting, as Windows 11 Settings draws it: a glyph, a title and a description on a card, and the setting's
// control on the right. The title, description and glyph come from settings.js, so search finds what the card
// shows. A card that opens something (a page under this one, or a page of Windows Settings) is a button itself.
Rectangle {
  id: card

  property string key
  readonly property var setting: Settings.setting(key)
  property string title: setting.title
  property string description: setting.description
  property string glyph: setting.glyph
  // A Windows Settings page the card opens, or a page under this one.
  readonly property string uri: setting.uri ?? ""
  readonly property bool opensPage: (setting.subpage ?? "") !== ""
  // Whether a click opens the Windows Settings page; a search result goes to the setting instead.
  property bool opensLink: uri !== ""
  property bool clickable: opensLink || opensPage
  // What search moves the keyboard to: the card's control, or the card when it is a button.
  property Item focusTarget: null
  // The controls on the right.
  default property alias control: trailing.data

  signal clicked()

  objectName: "setting:" + key
  width: parent ? parent.width : 0
  radius: Theme.controlCornerRadius
  color: !clickable ? Theme.cardBackgroundFillColorDefault
    : mouse.pressed ? Theme.controlFillColorTertiary
    : mouse.containsMouse ? Theme.controlFillColorSecondary
    : Theme.cardBackgroundFillColorDefault
  border.width: 1
  border.color: Theme.cardStrokeColorDefault
  activeFocusOnTab: clickable
  Accessible.role: clickable ? (opensLink ? Accessible.Link : Accessible.Button) : Accessible.Grouping
  Accessible.name: title
  Accessible.description: description
  Accessible.onPressAction: if (clickable) card.activate()

  Behavior on color { ColorAnimation { duration: Theme.controlFasterAnimationDuration } }

  function activate() {
    if (opensLink)
      Qt.openUrlExternally(uri)
    clicked()
  }

  // Moves the keyboard to this setting, and scrolls it into view.
  function focusSetting() {
    if (focusTarget)
      focusTarget.forceActiveFocus()
    else
      forceActiveFocus()
    reveal()
  }

  // Scrolls the page so the card is in view; a control on the card calls it when it takes the keyboard.
  function reveal() {
    for (let p = parent; p; p = p.parent) {
      if (p.objectName === "settingsScroll") {
        p.reveal(card)
        return
      }
    }
  }

  onActiveFocusChanged: if (activeFocus) reveal()
  Keys.onReturnPressed: if (clickable) activate()
  Keys.onEnterPressed: if (clickable) activate()
  Keys.onSpacePressed: if (clickable) activate()

  MouseArea {
    id: mouse
    anchors.fill: parent
    enabled: card.clickable
    hoverEnabled: true
    onClicked: card.activate()
  }

  // The focus rectangle Windows draws around a focused card.
  Rectangle {
    anchors.fill: parent
    anchors.margins: -3
    visible: card.activeFocus
    radius: card.radius + 3
    color: "transparent"
    border.width: 2
    border.color: Theme.focusStrokeColorOuter
  }

  // The glyph and the text on the left, the controls on the right. On a narrow card the controls go under the
  // text, as Windows 11 Settings does.
  readonly property real textX: glyph !== "" ? 52 : 16
  readonly property real endX: width - 16 - (clickable ? 28 : 0)
  readonly property bool stacked: trailing.width > 0 && endX - textX - trailing.width - 16 < 160

  implicitHeight: stacked ? texts.height + trailing.height + 32
    : Math.max(description !== "" ? 68 : 48, Math.max(texts.height, trailing.height) + 24)

  Text {
    x: 16
    y: card.stacked ? 12 : (card.height - height) / 2
    visible: card.glyph !== ""
    width: 20
    text: card.glyph
    horizontalAlignment: Text.AlignHCenter
    font.family: Theme.iconFontFamily
    font.pixelSize: 20
    color: Theme.textFillColorPrimary
    Accessible.ignored: true
  }

  Column {
    id: texts
    x: card.textX
    y: card.stacked ? 12 : (card.height - height) / 2
    width: (card.stacked || trailing.width === 0 ? card.endX : card.endX - trailing.width - 16) - card.textX

    Text {
      objectName: "cardTitle"
      width: parent.width
      text: card.title
      wrapMode: Text.Wrap
      font: Theme.typography.body
      color: card.enabled ? Theme.textFillColorPrimary : Theme.textFillColorDisabled
    }

    Text {
      objectName: "cardDescription"
      width: parent.width
      visible: card.description !== ""
      text: card.description
      wrapMode: Text.Wrap
      font: Theme.typography.caption
      color: card.enabled ? Theme.textFillColorSecondary : Theme.textFillColorDisabled
    }
  }

  Row {
    id: trailing
    x: card.stacked ? card.textX : card.endX - width
    y: card.stacked ? texts.y + texts.height + 8 : (card.height - height) / 2
    spacing: 8
  }

  // A page under this one, or a page in Windows Settings.
  Text {
    x: card.width - 16 - width
    anchors.verticalCenter: parent.verticalCenter
    visible: card.clickable
    text: card.opensLink ? "\uE8A7" : "\uE76C"
    font.family: Theme.iconFontFamily
    font.pixelSize: 12
    color: Theme.textFillColorPrimary
    Accessible.ignored: true
  }
}
