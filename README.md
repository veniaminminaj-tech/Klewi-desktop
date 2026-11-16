# Klewi Desktop

Klewi Code for Windows, built with C# WPF and .NET 10. The single setup installer includes AI coding features and the KCMODULE package manager.

## Features
- Dark UI with blue accents and a Klewi mascot with glowing green square eyes
- Real AI chat through Gemini, OpenAI, and OpenRouter-compatible chat APIs
- OpenRouter public model catalog browser
- API key protected with Windows DPAPI
- Project folder picker, file tree, and editable source/text-file editor with extension-based recognition for many common languages
- Multilingual Code Assistant with auto-detection or explicit language preference across mainstream and specialist languages
- Create new project files and save edits locally; automatic timestamped backups are stored under the user's LocalAppData folder
- Ask AI to rewrite the selected file; review the before/after content and explicitly approve before the app writes it
- Explicitly attach the current editor contents to the next AI chat request (100 KB disk-file limit for normal attachment; common secret filenames are blocked)

## Requirements
- Windows 10/11
- .NET 10 SDK

## Run
```powershell
dotnet run --project src/KlewiDesktop/KlewiDesktop.csproj
```

Open **Settings**, enter an API key for your selected provider, save it securely, then test the connection.

## Prototype limitations
The project editor currently handles one selected text/source file at a time; it does not yet perform autonomous multi-file edits. AI edits are sent to the configured external AI provider and are only written locally after the user reviews and approves the proposed replacement. Before saving, Klewi checks for outside changes and creates a backup. Common secret filenames are blocked, but this is not a complete secret scanner; review file contents before sending them to an external AI provider.

Minecraft Studio can run the Gradle `build` task for a generated project when a Gradle wrapper or system Gradle is available. Gradle build scripts execute code, so the app requires explicit confirmation and users should inspect generated build files first. A successful Gradle exit does not independently prove loader compatibility or guarantee the JAR works in-game. Plugin SDK, GitHub integration, Computer Control, and automated tests are not implemented yet. File context is only sent when the user explicitly selects a supported file and attaches it, or asks AI to edit that selected file. The app blocks common secret filenames, but this is not a complete secret scanner; review file contents before sending them to an external AI provider. OpenAI-compatible providers require a valid API key and model ID. OpenRouter model discovery reads its public catalog; actual inference may incur provider charges. The terminal intentionally permits only a small set of commands.
\n\n## ISO Tools\nThe ISO workspace can inspect an existing `.iso` file, calculate its SHA-256 checksum, and mount or dismount it as a virtual DVD on Windows 10/11. The Windows version selector is a label for organizing the selected image; it does not identify or validate the actual Windows edition inside the ISO. The app does not modify ISO contents or create/format bootable USB drives.\n

## Multilingual Code Assistant
Klewi can discuss and generate code across a broad range of languages. The chat language selector can auto-detect or prioritize a chosen language, and the editor identifies common languages from file extensions. Actual quality and framework knowledge depend on the selected AI model/provider; this does not guarantee support for every language, compiler, or runtime. The editor is a text editor, not a full IDE with built-in language servers, syntax highlighting, or every compiler installed.


## KCMODULE format

Klewi extensions use the `.kcmodule` package format. A package is a ZIP container with the `.kcmodule` extension and contains a `manifest.json` plus its module payload and documentation. The manifest declares the module ID, display name, version, minimum Klewi version, entry point and permissions. Modules are distributed as packages, not as separate EXE installers.

The first package descriptors are in `modules/MinecraftStudio` and `modules/IsoTools`. The main workspace no longer shows Minecraft Studio or ISO Tools as built-in navigation sections. The current packages establish the format and module metadata; a runtime loader and migration of the existing feature code into independently loadable module assemblies are still pending.


## One installer and Module Market

The release workflow creates one `Klewi-Code-Setup.exe` installer for the complete Klewi Code app. AI chat and code assistance are built into the app; provider API keys are configured in Settings.

The **Module Market** page can install validated `.kcmodule` ZIP packages into `%LOCALAPPDATA%\\Klewi\\Modules` and list installed packages. The setup also places bundled starter packages in `%APPDATA%\\Klewi\\ModuleMarket`.

Current limitation: the market supports local package installation and inventory only. It does not yet download from a hosted online catalog or execute third-party module code. The installer is a single app installer, not separate installers for modules.
