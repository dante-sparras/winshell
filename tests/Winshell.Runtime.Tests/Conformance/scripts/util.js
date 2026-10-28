.pragma library
.import "math.js" as MathUtil

var calls = 0
const version = "1.0"

function greet(name) {
    calls++
    return "hello " + name + " " + MathUtil.square(calls)
}
