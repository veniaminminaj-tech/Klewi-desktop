# Klewi Desktop

Windows desktop prototype built with C# WPF and .NET 10.

## Features
- Dark UI with blue accents and a Klewi mascot with glowing green square eyes
- Real AI chat through Gemini, OpenAI, and OpenRouter-compatible chat APIs
- OpenRouter public model catalog browser
- API key protected with Windows DPAPI
- Project folder picker, file tree, and editable source/text-file editor
- Create new project files and save edits locally; automatic timestamped backups are stored under the user's LocalAppData folder
- Ask AI to rewrite the selected file; review the before/after content and explicitly approve before the app writes it
- Explicitly attach the current editor contents to the next AI chat request (100 KB disk-file limit for normal attachment; common secret filenames are blocked)
- Minecraft Studio generator that writes Java mod/resource-pack project files, exports real pixel-art PNG textures, previews generated textures, and creates ZIP archives
- Optional Gradle `build` task for a selected generated mod project, with a confirmation warning, captured build output, and discovered JAR paths
- Restricted developer terminal

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

Minecraft Studio can run the Gradle `build` task for a generated project when a Gradle wrapper or system Gradle is available. Gradle build scripts execute code, so the app requires explicit confirmation and users should inspect generated build files first. A successful Gradle exit does not independently prove loader compatibility or guarantee the JAR works in-game. Plugin SDK, GitHub integration, Computer Control, installer, and automated tests are not implemented yet. File context is only sent when the user explicitly selects a supported file and attaches it, or asks AI to edit that selected file. The app blocks common secret filenames, but this is not a complete secret scanner; review file contents before sending them to an external AI provider. OpenAI-compatible providers require a valid API key and model ID. OpenRouter model discovery reads its public catalog; actual inference may incur provider charges. The terminal intentionally permits only a small set of commands.
