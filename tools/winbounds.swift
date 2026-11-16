// Prints "<windowID> <x> <y> <w> <h>" (points, top-left origin) for on-screen windows whose owner matches argv[1].
import CoreGraphics
import Foundation
let want = CommandLine.arguments.count > 1 ? CommandLine.arguments[1].lowercased() : ""
let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
for w in list {
    let owner = (w[kCGWindowOwnerName as String] as? String) ?? ""
    guard owner.lowercased().contains(want) else { continue }
    let b = w[kCGWindowBounds as String] as? [String: Any] ?? [:]
    let x = (b["X"] as? Double) ?? 0, y = (b["Y"] as? Double) ?? 0, wd = (b["Width"] as? Double) ?? 0, ht = (b["Height"] as? Double) ?? 0
    if wd < 200 || ht < 200 { continue }
    print("\(w[kCGWindowNumber as String] ?? 0) \(Int(x)) \(Int(y)) \(Int(wd)) \(Int(ht))")
}
