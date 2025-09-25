## [v0.3.0](https://github.com/DearVa/Everywhere/releases/tag/v0.3.0) - 2025-09-24

### ✨ New Features
- Added OOBE (Out-Of-Box Experience) for first time users, which includes:
  - Welcome Dialog
  - Quick Setup Wizard
- Added supported for custom model
- Added database for storing chat attachments
- Added support for more hotkey, such as `Copilot` key on Windows
- Added watchdog process to monitor the main process and clean up zombie processes

### 🔄️ Changed
- Refactored Plugin System, which includes:
  - Added Plugin Manager in Settings
  - Refactored web search plugin
  - Added file system plugin for reading and writing files
  - Added code execution plugin with PowerShell on Windows
  - Added web browsing plugin with Puppeteer
  - Added visual element plugin for capturing screen content when UI automation is not available
- Refactored logging system with structured logging
- Improved visual capturing performance
- Chat window now can be resized

### 🐞 Fixed
- Fixed removing or switching chat history frequently may cause crash
- Fixed emoji rendering issues in the chat window
- Fixed application may freeze when active on own window
- Fixed settings load/save issues
- Fixed the background of tray icon menu is transparent
- Fixed new chat button disable state is not updated when switching chat history



## [v0.2.4](https://github.com/DearVa/Everywhere/releases/tag/v0.2.4) - 2025-08-15

### ✨ New Features
- Added Change Log in Welcome Dialog

### 🔄️ Changed
- Apply warning level filter to EF Core logging

### 🐞 Fixed
- Fixed Google Gemini invoking issues
- Fixed Restart as Administrator may not work on some cases
- Fixed Dialog and Toast may crash the app when reopen after closed a window
- Fixed `ChatElementAttachment`'s overlay window may cover the `ChatFloatingWindow`
- Fixed `ChatElementAttachment`'s overlay window may not disappear

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.2.3...v0.2.4



## [v0.2.3](https://github.com/DearVa/Everywhere/releases/tag/v0.2.3) - 2025-08-14

### ✨ New Features
- Added settings for automatically startup
- Added settings for Software Update

### 🐞 Fixed
- Fixed markdown rendering issues in the Chat Window

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.2.2...v0.2.3



## [v0.2.2](https://github.com/DearVa/Everywhere/releases/tag/v0.2.2) - 2025-08-11

### ✨ New Features
- **Model Support**: Added support for `Claude Opus 4.1`

### 🔄️ Changed
- Split settings into separate sidebar items

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.2.1...v0.2.2



## [v0.2.1](https://github.com/DearVa/Everywhere/releases/tag/v0.2.1) - 2025-08-11

### ✨ New Features
- **Model Support**: Added support for `GPT-5` series models:
  - `GPT-5`
  - `GPT-5 mini`
  - `GPT-5 nano`

### 🐞 Fixed
- Fixed markdown rendering issues in the Chat Window

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.2.0...v0.2.1



## [v0.2.0](https://github.com/DearVa/Everywhere/releases/tag/v0.2.0) - 2025-08-10

This update introduces support for over 20 new models and a completely refactored settings page for a better user experience.

### ✨ New Features

We've integrated the following new models:

- **OpenAI**: `o4-mini`, `o3`, `GPT-4.1`, `GPT-4.1 mini`, `GPT-4o` (`GPT-5` series will be released in next version)
- **Anthropic**: `Claude Opus 4`, `Claude Sonnet 4`, `Claude 3.7 Sonnet`, `Claude 3.5 Haiku`
- **Google**: `Gemini 2.5 Pro`, `Gemini 2.5 Flash`, `Gemini 2.5 Flash-Lite`
- **DeepSeek**: `DeepSeek V3`, `DeepSeek R1`
- **Moonshot**: `Kimi K2`, `Kimi Latest`, `Kimi Thinking Preview`
- **xAI**: `Grok 4`, `Grok 3 Mini`, `Grok 3`
- **Ollama**: `GPT-OSS 20B`, `DeekSeek R1 7B`, `Qwen 3 8B`

### ⚠️ BREAKING CHANGE: Database Refactor

To improve performance and stability, the chat database has been refactored.

- **As this is a beta release, chat history from previous versions is no longer available.**
- The new database structure now supports data migrations, which will prevent data loss in future updates. We appreciate your understanding.

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.1.3...v0.2.0



## [v0.1.3](https://github.com/DearVa/Everywhere/releases/tag/v0.1.3) - 2025-08-08

### ✨ New Features
- Added a pin button to the Chat Window, to keep it always on top and not close on lost focus
- Added detailed error messages in the Chat Window
- Added auto enum settings support by @SlimeNull in #10

### 🐞 Fixed
- Fixed ChatInputBox max height

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.1.2...v0.1.3



## [v0.1.2](https://github.com/DearVa/Everywhere/releases/tag/v0.1.2) - 2025-08-02

### ✨ New Features
- Added a notification when the app is first hide to the system tray

### 🔄️ Changed
- (Style) Decreased the background opacity of the main window, for Mica effect

### 🐞 Fixed
- Fixed wrong links in Welcome Dialog

### ⚠️ Known Issues
- The opacity of tray icon menu is broken

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.1.1...v0.1.2



## [v0.1.1](https://github.com/DearVa/Everywhere/releases/tag/v0.1.1) - 2025-07-31

### ✨ New Features
- Added Logging

### 🗑️ Removed
- Removed custom window corner radius (Too many bugs, not worth it)

### 🐞 Fixed
- Fixed I18N not working when Language is not set

**Full Changelog**: https://github.com/DearVa/Everywhere/compare/v0.1.0...v0.1.1



## [v0.1.0](https://github.com/DearVa/Everywhere/releases/tag/v0.1.0) - 2025-07-31

### First Release · 万物生于有，有生于无。
