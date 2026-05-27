# LuxaTeams

Syncs your Microsoft Teams presence to a Luxafor light. Red when you're in a call or busy, yellow when away, green when available. Runs in the system tray and can start with Windows. **NO MICROSOFT SIGN-IN!**

## How it works

The new Teams doesn't expose a local presence API, so LuxaTeams reads the Teams cache (`EBWebView`) directly. It scans recently modified files for the `availability` and `activity` fields and maps them to a color. No token, no Microsoft sign-in; nothing leaves your machine except the color sent to the Luxafor webhook.

## Presence states and colors

These are all the Teams states LuxaTeams recognizes. Any state not listed falls back to green.

| Teams state | Meaning | Color |
|---|---|---|
| `Available` | Available | Green |
| `Busy` | Busy | Red |
| `DoNotDisturb` | Do not disturb | Red |
| `InACall` | In a call | Red |
| `InAConferenceCall` | In a conference call | Red |
| `OnThePhone` | On the phone | Red |
| `InAMeeting` | In a meeting | Red |
| `Presenting` | Presenting / sharing screen | Red |
| `Away` | Away | Yellow |
| `BeRightBack` | Be right back | Yellow |

## Requirements

- Windows 10 / 11
- .NET Framework 4.7.2+ (also builds on .NET 6+ with WinForms)
- The new Microsoft Teams (the `MSTeams_*` Store app), running. Classic Teams is not supported.
- A Luxafor light and its webhook User ID

## Getting the Luxafor User ID

Open the Luxafor app, go to the Webhook section, enable it, and copy the User ID.

## Setup

1. Build and run `LuxaTeams.exe`.
2. Paste your Luxafor User ID and set the polling interval (5 s is fine).
3. Click Start.
4. Optionally tick "start with Windows" to launch hidden in the background.

Closing the window minimizes to the tray. Double-click the tray icon to reopen it.

## Build

```
git clone https://github.com/brunopaiva15/LuxaTeams.git
cd LuxaTeams
dotnet build -c Release
```

Or open the solution in Visual Studio and build in Release.

## Notes

Settings are stored under `HKCU\Software\LuxaTeams`; the startup entry uses the standard `...\CurrentVersion\Run` key. Detection depends on the internal layout of the Teams cache, so a major Teams update could change the format and break it until the regexes are updated.

## License

MIT. Not affiliated with Microsoft or Luxafor.