import QtQuick.Controls

// Keyboard focus must bring its row into view, including on small work areas at high DPI.
ScrollView {
  function reveal(item) {
    const top = item.mapToItem(contentItem, 0, 0).y
    const bottom = top + item.height
    const delta = top < 4 ? top - 4 : bottom > availableHeight - 4 ? bottom - availableHeight + 4 : 0
    contentItem.contentY = Math.max(0, Math.min(Math.max(0, contentHeight - availableHeight), contentItem.contentY + delta))
  }
}
