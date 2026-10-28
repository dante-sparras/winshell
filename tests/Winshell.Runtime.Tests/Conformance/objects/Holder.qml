import QtQml

QtObject {
    enum Mode { Off, On = 5, Auto }
    default property list<QtObject> items
}
