# Klewi Desktop

Windows desktop prototype built with C# WPF and .NET 10.

## Features
- Dark UI with blue accents
- Real AI chat through Gemini, OpenAI, and OpenRouter-compatible chat APIs\n- OpenRouter public model catalog browser
- API key protected with Windows DPAPI
- Project folder picker and file browser
- Restricted developer terminal

## Requirements
- Windows 10/11
- .NET 10 SDK

## Run
```powershell
dotnet run --project src/KlewiDesktop/KlewiDesktop.csproj
```

Open **Settings**, enter a Gemini API key, save it securely, then test the connection.

## Prototype limitations
Autonomous file-editing agent, plugin SDK, GitHub integration, Computer Control, installer, and automated tests are not implemented yet. OpenAI-compatible providers require a valid API key and model ID. OpenRouter model discovery reads its public catalog; actual inference may incur provider charges. The terminal intentionally permits only a small set of commands.
