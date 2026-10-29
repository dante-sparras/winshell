# Changelog

## [0.6.1](https://github.com/dante-sparras/winshell/compare/v0.6.0...v0.6.1) (2026-10-29)


### Bug Fixes

* attach release assets before publishing drafts ([#179](https://github.com/dante-sparras/winshell/issues/179)) ([75b2c05](https://github.com/dante-sparras/winshell/commit/75b2c05d6a1056d054e15f88015b48f4cce4c7e7))
* **cli:** a bad --pid or --tail exits 2 ([c3041af](https://github.com/dante-sparras/winshell/commit/c3041afa77cb820d5372105de4658382335c2f97))
* keep media cache cleanup inside real directories ([#178](https://github.com/dante-sparras/winshell/issues/178)) ([2fe982a](https://github.com/dante-sparras/winshell/commit/2fe982a699e829b937716f2c6f7759de219c769b))
* keep window resources alive until its frame returns ([#177](https://github.com/dante-sparras/winshell/issues/177)) ([1400438](https://github.com/dante-sparras/winshell/commit/14004386d449b43ebd1843cb546188bce572da23))

## [0.6.0](https://github.com/dante-sparras/winshell/compare/v0.5.0...v0.6.0) (2026-10-29)


### ⚠ BREAKING CHANGES

* **config:** user config files are TOML

### Features

* **cli:** winshell doctor checks the setup and says how to fix what's wrong ([#157](https://github.com/dante-sparras/winshell/issues/157)) ([884b0a1](https://github.com/dante-sparras/winshell/commit/884b0a16bf01c58c28ce2a54a92a05ef4aa39938))
* **config:** user config files are TOML ([3876ef7](https://github.com/dante-sparras/winshell/commit/3876ef7b02706fc27123e9484a2fb3cf513e9a58))
* **install:** add a per-user PowerShell install command ([8fe5158](https://github.com/dante-sparras/winshell/commit/8fe515822fb0054715d814e288b2f7df11d0c6e4))
* **release:** a portable zip and a winget manifest with each release ([#121](https://github.com/dante-sparras/winshell/issues/121)) ([83eef64](https://github.com/dante-sparras/winshell/commit/83eef6422ce2c6a1a880dd4378485807d47f553f)), closes [#117](https://github.com/dante-sparras/winshell/issues/117)
* **release:** sign release binaries through SignPath Foundation once its project is set up ([#145](https://github.com/dante-sparras/winshell/issues/145)) ([4417ad8](https://github.com/dante-sparras/winshell/commit/4417ad865e22bbaf24ca9b60841b5069b4eb64d9))
* **runtime:** crash reports, and a crashed window opens again ([#122](https://github.com/dante-sparras/winshell/issues/122)) ([71bc9bb](https://github.com/dante-sparras/winshell/commit/71bc9bbb52e99c3f2a32fc7e728dbce450692845))
* **shell:** a Windows 11 notice for a config error or a window that failed ([#148](https://github.com/dante-sparras/winshell/issues/148)) ([4e86c8b](https://github.com/dante-sparras/winshell/commit/4e86c8b21f4bcc484d330a06fc2bf5a9390afa33))
* **store:** an MSIX package for the Microsoft Store, built in CI ([#141](https://github.com/dante-sparras/winshell/issues/141)) ([80169d4](https://github.com/dante-sparras/winshell/commit/80169d4af21d33597a50712e05ac51d176dde612)), closes [#119](https://github.com/dante-sparras/winshell/issues/119)
* **updates:** the portable build updates itself from GitHub Releases ([#136](https://github.com/dante-sparras/winshell/issues/136)) ([16ff0e6](https://github.com/dante-sparras/winshell/commit/16ff0e6b43638003f794fcbdfe1299aedb9ee1a7)), closes [#120](https://github.com/dante-sparras/winshell/issues/120)


### Bug Fixes

* **instances:** list a reused process id once ([#125](https://github.com/dante-sparras/winshell/issues/125)) ([b66d766](https://github.com/dante-sparras/winshell/commit/b66d76625fbe8db9f840dcbb760887432680f373))
* **media:** keep a session when Windows returns a new object for it ([#126](https://github.com/dante-sparras/winshell/issues/126)) ([4dd909f](https://github.com/dante-sparras/winshell/commit/4dd909f39d26e2a986708a1dd1ebdad9e095136a))
* **shell:** notification times follow the regional short time ([03dd86b](https://github.com/dante-sparras/winshell/commit/03dd86b3cf0988b4fdf2a41aaa95ccc864fdce7c))

## [0.5.0](https://github.com/dante-sparras/winshell/compare/v0.4.0...v0.5.0) (2026-10-29)


### Features

* **shell:** a Windows 11 settings app for the default shell, Winshell Settings ([#112](https://github.com/dante-sparras/winshell/issues/112)) ([f088b7a](https://github.com/dante-sparras/winshell/commit/f088b7a2ee5c01d7dc52d8ba7949c318fbf37f19))
* **shell:** a Windows 11 taskbar on every screen ([#98](https://github.com/dante-sparras/winshell/issues/98)) ([a8b7475](https://github.com/dante-sparras/winshell/commit/a8b7475f1e0b0fc5dc13c7dce161d3543d1aa1f9))
* **shell:** add a Windows 11 Start launcher ([#93](https://github.com/dante-sparras/winshell/issues/93)) ([9ab2698](https://github.com/dante-sparras/winshell/commit/9ab2698ac9f23ac49537720668950d43380a578d))
* **shell:** add notification center and regional calendar ([daf1ff3](https://github.com/dante-sparras/winshell/commit/daf1ff3d3e453cea3e66d9c09da66dbb4b4002c3))
* **shell:** add quick settings with tiles, volume, brightness and battery ([#87](https://github.com/dante-sparras/winshell/issues/87)) ([1f4327b](https://github.com/dante-sparras/winshell/commit/1f4327b8a07aa8f879b1d740c93b3e906370e6f9))
* **shell:** edit, reorder and keep the quick settings tiles ([#95](https://github.com/dante-sparras/winshell/issues/95)) ([75c85ce](https://github.com/dante-sparras/winshell/commit/75c85cefc8665a0bd46efecb45fb5f16dde2b375))
* **shell:** pick the sound output and control media from quick settings ([#90](https://github.com/dante-sparras/winshell/issues/90)) ([08e1e67](https://github.com/dante-sparras/winshell/commit/08e1e6715b2aa12efbebb6f137e62ea09b9a3439))
* **shell:** run winshell at sign-in when the user turns it on ([#104](https://github.com/dante-sparras/winshell/issues/104)) ([b65cd2b](https://github.com/dante-sparras/winshell/commit/b65cd2bb1d0cb15ddda88f1cc4cb39f80999e34a))
* **shell:** show an on-screen display for volume, brightness, media and mute ([#94](https://github.com/dante-sparras/winshell/issues/94)) ([a12ae91](https://github.com/dante-sparras/winshell/commit/a12ae91018aea114ed1d07ba4a08bee1551c4182))
* **theme:** a shell can choose light or dark and its own accent ([#101](https://github.com/dante-sparras/winshell/issues/101)) ([91e2dda](https://github.com/dante-sparras/winshell/commit/91e2dda7c7f9f2f97d383e23f2e0175c4ea9b4c1))
* **windows:** live window previews drawn by DWM ([#91](https://github.com/dante-sparras/winshell/issues/91)) ([dce0586](https://github.com/dante-sparras/winshell/commit/dce0586b879eebd9fd57f3e1d516532300889760))
* **windows:** show which app windows flash for attention ([#86](https://github.com/dante-sparras/winshell/issues/86)) ([be560d4](https://github.com/dante-sparras/winshell/commit/be560d472802a53b5df3fb6bc05f83a6203d4e99))


### Bug Fixes

* **cli:** a recycled instance process id no longer crashes winshell ([#83](https://github.com/dante-sparras/winshell/issues/83)) ([065c0d8](https://github.com/dante-sparras/winshell/commit/065c0d81ea0c866ff56562578e85e0db66eaa29b))
* **qml:** a component's own children no longer go to the default property it declares ([#108](https://github.com/dante-sparras/winshell/issues/108)) ([ba86ff5](https://github.com/dante-sparras/winshell/commit/ba86ff560da8f83f3cd48084d4909f1601464cbc))
* **qml:** a value a document assigns replaces the binding its component declared ([#110](https://github.com/dante-sparras/winshell/issues/110)) ([83f3858](https://github.com/dante-sparras/winshell/commit/83f38586d16a418ed48fe4db13e8eb7b7461a379))
* **runtime:** positioners and layouts follow a repeater that reorders its rows ([#92](https://github.com/dante-sparras/winshell/issues/92)) ([ced75e8](https://github.com/dante-sparras/winshell/commit/ced75e840b3394989ef1210fa96e7486a1546b16))
* **shell:** a large window group fits its taskbar preview ([#107](https://github.com/dante-sparras/winshell/issues/107)) ([c6bd815](https://github.com/dante-sparras/winshell/commit/c6bd81506582287462d19b040d4618765bbf1445))
* **shell:** a taskbar pin made while the saved pins load is kept ([#100](https://github.com/dante-sparras/winshell/issues/100)) ([468bcdc](https://github.com/dante-sparras/winshell/commit/468bcdc2f10c95497e00aba370edd3302a4534be))
* **shell:** an auto-hiding taskbar stays on its own screen ([#103](https://github.com/dante-sparras/winshell/issues/103)) ([0836cd8](https://github.com/dante-sparras/winshell/commit/0836cd855880ac962579a75cca662e6934ece35f))
* **shell:** taskbar apps that do not fit scroll instead of running under the tray ([#106](https://github.com/dante-sparras/winshell/issues/106)) ([ce290a6](https://github.com/dante-sparras/winshell/commit/ce290a633d5d0d7bb79df40fb4bfd9febf6509c9))
* **shell:** taskbar buttons stay put while other apps come and go ([#105](https://github.com/dante-sparras/winshell/issues/105)) ([e9613ee](https://github.com/dante-sparras/winshell/commit/e9613ee6c3767d9e1d089a30aab82b66151a575d))
* **shell:** the taskbar works from the keyboard and follows Narrator's focus ([#109](https://github.com/dante-sparras/winshell/issues/109)) ([0c1aa8a](https://github.com/dante-sparras/winshell/commit/0c1aa8ab7f5e45e7e6270cbb8028307e7a78c293))
* **tray:** the shell's app bars reserve their edge while it hosts the tray ([#97](https://github.com/dante-sparras/winshell/issues/97)) ([a4a72ab](https://github.com/dante-sparras/winshell/commit/a4a72ab4e78f46f3c129d875ca730fc6b9bf1b96))
* **windows:** a panel asks for its edge again when the app bar it yielded to is gone ([#102](https://github.com/dante-sparras/winshell/issues/102)) ([1a21d1c](https://github.com/dante-sparras/winshell/commit/1a21d1cc8e18a150a873c59349c793c358f1fc5c))
* **windows:** a panel that reserves an edge no longer places itself over and over ([#96](https://github.com/dante-sparras/winshell/issues/96)) ([f8ee427](https://github.com/dante-sparras/winshell/commit/f8ee4275002423090eb983b89967e069f357c026))

## [0.4.0](https://github.com/dante-sparras/winshell/compare/v0.3.0...v0.4.0) (2026-10-28)


### ⚠ BREAKING CHANGES

* **shell:** report the winshell release from hasVersion ([#78](https://github.com/dante-sparras/winshell/issues/78))
* **tray:** send clicks the way Explorer does, without taking the keyboard ([#77](https://github.com/dante-sparras/winshell/issues/77))
* **session:** hand locking to Windows and remove the shell lock screen ([#75](https://github.com/dante-sparras/winshell/issues/75))
* **tray:** name tray icons for Windows, with overflow, hidden icons and balloons ([#76](https://github.com/dante-sparras/winshell/issues/76))
* **config:** load configurations from roaming app data

### Features

* **config:** load configurations from roaming app data ([94eea6c](https://github.com/dante-sparras/winshell/commit/94eea6ca419381a542cb78513a81458fb5d629e1))
* **session:** hand locking to Windows and remove the shell lock screen ([#75](https://github.com/dante-sparras/winshell/issues/75)) ([f07bfed](https://github.com/dante-sparras/winshell/commit/f07bfed66543a6fe185672d7f91c35524c4565f9))
* **tray:** draw tray icons sharp at every monitor scale ([#79](https://github.com/dante-sparras/winshell/issues/79)) ([31b6f9f](https://github.com/dante-sparras/winshell/commit/31b6f9f0ed578f7223e66523506c6d8919644539))
* **tray:** name tray icons for Windows, with overflow, hidden icons and balloons ([#76](https://github.com/dante-sparras/winshell/issues/76)) ([8d35f8f](https://github.com/dante-sparras/winshell/commit/8d35f8f2b73d62aa5d7e4394b8617530f874143e))


### Bug Fixes

* **shell:** report the winshell release from hasVersion ([#78](https://github.com/dante-sparras/winshell/issues/78)) ([151a17b](https://github.com/dante-sparras/winshell/commit/151a17bc9111d249211f913c71d716fa71c9b3bd))
* **tray:** send clicks the way Explorer does, without taking the keyboard ([#77](https://github.com/dante-sparras/winshell/issues/77)) ([c33b8f0](https://github.com/dante-sparras/winshell/commit/c33b8f05b6576a6e1b7e36f36251085f07eb05f4))
* **tray:** survive Explorer restarts and hand the tray over between instances ([#71](https://github.com/dante-sparras/winshell/issues/71)) ([b15bc2b](https://github.com/dante-sparras/winshell/commit/b15bc2ba3cf79e3a4a705fcf5c2c98d0e9e8f965))

## [0.3.0](https://github.com/dante-sparras/winshell/compare/v0.2.0...v0.3.0) (2026-10-28)


### ⚠ BREAKING CHANGES

* **input:** register global hotkeys from qml
* **apps:** index Start menu apps with search and usage
* **power:** name the battery, power mode, and monitor brightness for Windows
* **media:** name system media sessions for Windows
* **bluetooth:** expose Windows state and explicit device actions ([#60](https://github.com/dante-sparras/winshell/issues/60))
* **windows:** name the window list for Windows and move windows between monitors ([#51](https://github.com/dante-sparras/winshell/issues/51))
* **network:** expose Windows profiles and explicit Wi-Fi actions ([#56](https://github.com/dante-sparras/winshell/issues/56))
* **audio:** name playback devices and sessions for Windows
* **notifications:** add a Windows notification center and native toast sender ([#50](https://github.com/dante-sparras/winshell/issues/50))
* **api:** windows-first module names ([#44](https://github.com/dante-sparras/winshell/issues/44))

### Features

* **api:** windows-first module names ([#44](https://github.com/dante-sparras/winshell/issues/44)) ([c4e38f2](https://github.com/dante-sparras/winshell/commit/c4e38f2061ad46d0177712adc6275f720cdaaaeb))
* **apps:** index Start menu apps with search and usage ([2e9deb7](https://github.com/dante-sparras/winshell/commit/2e9deb74921c8a6dd8fdfe8e191a43fb0521ebd2))
* **audio:** name playback devices and sessions for Windows ([93525d0](https://github.com/dante-sparras/winshell/commit/93525d0c43176c67136c4ba8322dd9779e40f104)), closes [#47](https://github.com/dante-sparras/winshell/issues/47)
* **bluetooth:** expose Windows state and explicit device actions ([#60](https://github.com/dante-sparras/winshell/issues/60)) ([c0927ca](https://github.com/dante-sparras/winshell/commit/c0927ca47ebf4cb7a48cc4949b2c040d2396ca2d))
* **capture:** add Windows screen capture and live previews ([#65](https://github.com/dante-sparras/winshell/issues/65)) ([e54df4d](https://github.com/dante-sparras/winshell/commit/e54df4d5a8550f5f2fb6eaaa34a265c32dbbb8e3))
* **input:** register global hotkeys from qml ([6fa5286](https://github.com/dante-sparras/winshell/commit/6fa528684b1ef4fb090ac12a6822bcf2b6e90b8d))
* **media:** name system media sessions for Windows ([f92b879](https://github.com/dante-sparras/winshell/commit/f92b879ee4d2f83934410820b6a3fb6b0d9f2e15)), closes [#54](https://github.com/dante-sparras/winshell/issues/54)
* **network:** expose Windows profiles and explicit Wi-Fi actions ([#56](https://github.com/dante-sparras/winshell/issues/56)) ([757b7c7](https://github.com/dante-sparras/winshell/commit/757b7c794f6797c486ca9cd4cbe29e9a31342a23))
* **notifications:** add a Windows notification center and native toast sender ([#50](https://github.com/dante-sparras/winshell/issues/50)) ([8fb4014](https://github.com/dante-sparras/winshell/commit/8fb40147a167d4dc55a41367ecedbd96c6b72f29))
* **power:** name the battery, power mode, and monitor brightness for Windows ([721e92f](https://github.com/dante-sparras/winshell/commit/721e92fb42e48b774986eed7f9ac4138336279ed)), closes [#58](https://github.com/dante-sparras/winshell/issues/58)
* **windows:** list and switch virtual desktops ([#59](https://github.com/dante-sparras/winshell/issues/59)) ([c0a3573](https://github.com/dante-sparras/winshell/commit/c0a357399aa3beabbdbf6afa94f05b65932c043f))
* **windows:** name the window list for Windows and move windows between monitors ([#51](https://github.com/dante-sparras/winshell/issues/51)) ([d010162](https://github.com/dante-sparras/winshell/commit/d0101623a855214e91b9a1c2382a52216ac4d253))

## [0.2.0](https://github.com/dante-sparras/winshell/compare/v0.1.6...v0.2.0) (2026-10-28)


### ⚠ BREAKING CHANGES

* **theme:** WindowsTheme color properties are renamed to their WinUI names, such as textPrimary to textFillColorPrimary.
* **platform:** place windows correctly across monitors of mixed dpi ([#28](https://github.com/dante-sparras/winshell/issues/28))

### Features

* a post-merge hook deletes landed feature branches ([5a4329e](https://github.com/dante-sparras/winshell/commit/5a4329e5a7dd5705c9882d8b362b5568f6e05a8a))
* add a hello-world app and its solution ([bbd6f25](https://github.com/dante-sparras/winshell/commit/bbd6f25ea1cf7de78717a1465ff5b13972c66fab))
* **ci:** pull requests need one aggregate check ([b58e090](https://github.com/dante-sparras/winshell/commit/b58e0909a1932c3ba48fd2fd492842907c6644fc))
* **controls:** draw buttons, check boxes, radio buttons and switches as winui 3 does ([#34](https://github.com/dante-sparras/winshell/issues/34)) ([2dc180f](https://github.com/dante-sparras/winshell/commit/2dc180f8446a954163ee73f87f6ad92b563b835a))
* **controls:** draw menus, flyouts and tool tips as winui 3 does ([#37](https://github.com/dante-sparras/winshell/issues/37)) ([78ef123](https://github.com/dante-sparras/winshell/commit/78ef1234e9d50a17a3397098c146b94e6476dad0)), closes [#32](https://github.com/dante-sparras/winshell/issues/32)
* **controls:** draw sliders, progress and scroll bars as winui 3 does ([#35](https://github.com/dante-sparras/winshell/issues/35)) ([02aa545](https://github.com/dante-sparras/winshell/commit/02aa545b692caad9d0d01c03acbcf2b91561cc56))
* **controls:** draw text boxes, combo boxes and list rows as winui 3 does ([#36](https://github.com/dante-sparras/winshell/issues/36)) ([a28cb39](https://github.com/dante-sparras/winshell/commit/a28cb39188d1c6cc955f2570966c80e66c79ab3a))
* expose controls to ui automation and navigate them with the keyboard ([#31](https://github.com/dante-sparras/winshell/issues/31)) ([0e9e596](https://github.com/dante-sparras/winshell/commit/0e9e5963a83e80f2ebe385558cc15178d8a58abb))
* **runtime:** native .net qml runtime with fluent rendering ([#22](https://github.com/dante-sparras/winshell/issues/22)) ([0549c29](https://github.com/dante-sparras/winshell/commit/0549c29e44c2994d0f6b05f58b585c80c4a0c80a))
* **text:** render text with directwrite ([#30](https://github.com/dante-sparras/winshell/issues/30)) ([3d4451c](https://github.com/dante-sparras/winshell/commit/3d4451c78af6daff8f11e1bf769b4bdaf77b92c1))
* the checkout is a Quickshell v0.3.2 reference ([096f772](https://github.com/dante-sparras/winshell/commit/096f77293d5c36903ffc8a9d9a69fe29d66b8b27))
* **theme:** take the fluent tokens from winui 3 and follow contrast themes ([#33](https://github.com/dante-sparras/winshell/issues/33)) ([c03ae42](https://github.com/dante-sparras/winshell/commit/c03ae42853fc8082fb5ffd98aba5dc65366e3d25))
* use the shared Hermes trunk workflow ([395fede](https://github.com/dante-sparras/winshell/commit/395fede1cb8b0edab680a4e1a05c7f153bb82cc1))


### Bug Fixes

* **ci:** let detect and history fetch origin ([008dafc](https://github.com/dante-sparras/winshell/commit/008dafcd20ca5f35bc600e8045bffe458ec16aa6))
* **ci:** the pre-push lint reads each tip from git ([2e65154](https://github.com/dante-sparras/winshell/commit/2e651548eb20ec26c4571cdd81556b86de8daa56))
* **platform:** place windows correctly across monitors of mixed dpi ([#28](https://github.com/dante-sparras/winshell/issues/28)) ([2b77655](https://github.com/dante-sparras/winshell/commit/2b77655460f93173e7df4ae5bfb167a482147c9b))
* **runtime:** keep instances apart and report a config that crashes the runtime ([#42](https://github.com/dante-sparras/winshell/issues/42)) ([4fc0cee](https://github.com/dante-sparras/winshell/commit/4fc0cee788893932e7d053eeda3ecbc017702d13))
* **runtime:** release each generation on reload ([#41](https://github.com/dante-sparras/winshell/issues/41)) ([9b8a139](https://github.com/dante-sparras/winshell/commit/9b8a13915de99dd3fc408ec75ce4584dc3770ca4))
