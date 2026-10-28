# Windows are DirectComposition surfaces with DWM backdrops

Each window is an HWND whose pixels live in a DirectComposition swap chain. Where the frame is transparent, the DWM shows nothing, Mica, Acrylic, or Mica Alt. That is the Windows 11 material behind a panel or a popup, without a XAML window hosting it.
