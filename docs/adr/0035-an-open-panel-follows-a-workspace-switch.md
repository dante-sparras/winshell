# An open panel stays through a workspace switch

A panel is Winoma's own window, and a Workspace switch hides only the windows of the Workspace that is left (ADR 0003). So a switch leaves an open panel where it is, and nothing has to pin it or hold it open. The panel keeps the keyboard: the switch shows the new Workspace's windows and gives none of them the keyboard, and the Workspace's window gets it when the panel closes. A window Winoma meets while the panel is open waits the same way, and hover focus waits too.

Each monitor has a Workspace in front (ADR 0079), and one panel is open at a time across all of them, on one monitor (ADR 0080). A switch on any monitor leaves it open, on the monitor it is on, whether or not that is the monitor whose Workspace changed. Only a press outside it closes it, and a press on another monitor, that monitor's Bar included, is one.

A panel that closes hands the keyboard on, and Windows may give it to a window Winoma keeps hidden. That window did not ask for it, so the Workspace in front takes the keyboard back (ADR 0038).

While one of Winoma's own windows has the keyboard, Windows does not call the keyboard hook for its keys if the same process also takes raw keyboard input. With a panel open, every chord would be lost until the panel closed. So the hook thread stops raw keyboard input while an Winoma window is in the foreground and starts it again when another window is. Raw input only feeds hook repair, and nothing goes past the hook while Winoma has the keyboard. A keyboard hook in its own process (ADR 0030) does not share the desktop's raw input, so it is not affected.
