# Changelog

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
