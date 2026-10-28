# Changelog

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
