.pragma library

const months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]

function pad(n) {
    return n < 10 ? "0" + n : "" + n
}

function time(date) {
    return pad(date.getHours()) + ":" + pad(date.getMinutes())
}

function day(date) {
    return date.getDate() + "." + (date.getMonth() + 1) + "." + date.getFullYear()
}

function month(date) {
    return months[date.getMonth()] + " " + date.getFullYear()
}

function cell(first, index) {
    const offset = (first.getDay() + 6) % 7
    return new Date(first.getFullYear(), first.getMonth(), 1 - offset + index)
}

function sameDay(a, b) {
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}
