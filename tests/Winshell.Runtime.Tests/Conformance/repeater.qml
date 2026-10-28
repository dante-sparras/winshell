// Repeater over a count, an array and a ListModel, with required properties, roles and model changes.
import QtQuick

Item {
    id: root

    ListModel {
        id: fruit
        ListElement { name: "apple"; cost: 2 }
        ListElement { name: "pear"; cost: 3 }
    }

    Row {
        Repeater {
            id: counted
            model: 3
            Item {
                required property int index
                objectName: "n" + index
            }
        }
    }

    Column {
        Repeater {
            id: listed
            model: fruit
            delegate: Text {
                required property string name
                required property int cost
                required property int index
                text: index + ":" + name + "=" + cost
            }
            onItemAdded: (index, item) => console.log("added", index, item.text)
            onItemRemoved: (index, item) => console.log("removed", index, item.text)
        }
    }

    Repeater {
        id: arrayed
        model: ["x", "y"]
        Item {
            required property string modelData
            objectName: modelData
        }
    }

    Component.onCompleted: {
        console.log("counted", counted.count, counted.itemAt(2).objectName)
        console.log("arrayed", arrayed.count, arrayed.itemAt(1).objectName)
        console.log("listed", listed.count, listed.itemAt(1).text)
        fruit.append({ name: "plum", cost: 5 })
        fruit.setProperty(0, "cost", 4)
        console.log("first", listed.itemAt(0).text)
        fruit.remove(1)
        console.log("listed", listed.count, listed.itemAt(1).text)
        counted.model = 1
        console.log("counted", counted.count)
    }
}

// expect: added 0 0:apple=2
// expect: added 1 1:pear=3
// expect: counted 3 n2
// expect: arrayed 2 y
// expect: listed 2 1:pear=3
// expect: added 2 2:plum=5
// expect: first 0:apple=4
// expect: removed 1 1:pear=3
// expect: listed 2 1:plum=5
// expect: counted 1
