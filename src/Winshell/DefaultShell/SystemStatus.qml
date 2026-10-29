pragma Singleton
import Winshell
import Winshell.Network
import Winshell.Audio
import Winshell.Power
import QtQuick

// What the taskbar's status icons and quick settings both show: the network, the volume and the battery, with the
// Segoe Fluent Icons glyph Windows 11 draws for each.
Singleton {
  readonly property var wifi: Networking.devices.values.find(d => d.type === DeviceType.Wifi) ?? null
  readonly property var wifiNetwork: wifi ? wifi.networks.values.find(n => n.connected) ?? null : null
  readonly property bool online: Networking.connectivity === NetworkConnectivity.InternetAccess
    || Networking.connectivity === NetworkConnectivity.LocalAccess
    || Networking.connectivity === NetworkConnectivity.CaptivePortal
  // Wi-Fi's signal in bars, from 1 to 4, as Windows draws it. No signal reading draws all four.
  readonly property int wifiBars: wifiNetwork && wifiNetwork.signalStrength > 0 ? Math.max(1, Math.min(4, Math.ceil(wifiNetwork.signalStrength * 4))) : 4
  // Wi-Fi when it is connected, with its bars; otherwise any connection is drawn as wired, as Windows does for virtual
  // adapters.
  readonly property string networkGlyph: wifi && wifi.connected ? ["", "", "", ""][wifiBars - 1] : online ? "" : ""
  // The bars Wi-Fi does not have, which Windows draws faintly under networkGlyph.
  readonly property string networkTrackGlyph: wifi && wifi.connected && wifiBars < 4 ? "" : ""

  readonly property var output: Audio.defaultPlaybackDevice
  readonly property real volume: output ? output.audio.volume : 0
  readonly property bool muted: output ? output.audio.muted : true
  readonly property string volumeGlyph: muted ? "" : volume === 0 ? "" : volume < 1 / 3 ? "" : volume < 2 / 3 ? "" : ""

  readonly property var battery: Power.displayDevice
  readonly property bool hasBattery: battery.isPresent
  readonly property int batteryPercent: Math.round(battery.percentage * 100)
  readonly property bool charging: battery.state === PowerDeviceState.Charging
  readonly property bool energySaver: PowerMode.effective === EffectivePowerMode.EnergySaver
  // Battery0 to Battery10, with the charging and energy saver sets, in tenths of the charge.
  readonly property string batteryGlyph: {
    const tenth = Math.max(0, Math.min(10, Math.round(battery.percentage * 10)))
    const glyphs = charging ? [0xE85A, 0xE85B, 0xE85C, 0xE85D, 0xE85E, 0xE85F, 0xE860, 0xE861, 0xE862, 0xE83E, 0xEA93]
      : energySaver ? [0xE863, 0xE864, 0xE865, 0xE866, 0xE867, 0xE868, 0xE869, 0xE86A, 0xE86B, 0xEA94, 0xEA95]
      : [0xE850, 0xE851, 0xE852, 0xE853, 0xE854, 0xE855, 0xE856, 0xE857, 0xE858, 0xE859, 0xE83F]
    return String.fromCharCode(glyphs[tenth])
  }
}
