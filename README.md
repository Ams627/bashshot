# bashshot

Copy the focused Git Bash window to the Windows clipboard as an image.
Success is silent. Paste the screenshot with Ctrl+V.

## Build and run

Extract this folder, open Git Bash in it, and run these commands with the .NET 10 SDK installed.

```bash
dotnet build -c Release
./bin/Release/net10.0-windows/bashshot.exe
```

To run it as `bashshot` from any directory, add the build output directory to
your PATH. Keep the output files together because the executable uses the
.NET Desktop Runtime.

## What it captures

The tool checks that the focused window is mintty, Windows Terminal, or the
classic Windows console. In Windows Terminal, the capture includes the whole
window and its tab bar. The capture includes the visible terminal contents,
including the command you typed, and can include overlapping windows. If part
of the terminal is off-screen, that part is cropped.
