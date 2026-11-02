# Klewi Desktop

Windows desktop prototype built with C# WPF and .NET 10.

## Features
- Dark UI with blue accents and a Klewi mascot with glowing green square eyes
- Real AI chat through Gemini, OpenAI, and OpenRouter-compatible chat APIs
- OpenRouter public model catalog browser
- API key protected with Windows DPAPI
- Project folder picker, file tree, and text-file preview
- Explicitly attach a selected source file to the next AI chat request (100 KB limit; common secret filenames are blocked)
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
Autonomous multi-file editing agent, plugin SDK, GitHub integration, Computer Control, installer, and automated tests are not implemented yet. File context is only sent when the user explicitly selects a supported file and clicks the attach button. The app blocks common secret filenames, but this is not a complete secret scanner; review file contents before sending them to an external AI provider. OpenAI-compatible providers require a valid API key and model ID. OpenRouter model discovery reads its public catalog; actual inference may incur provider charges. The terminal intentionally permits only a small set of commands.
