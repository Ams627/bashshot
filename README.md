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

## Prebuilt exe

GitHub Actions publishes a single-file `bashshot.exe` (win-x64) on every push
to `main` and on pull requests; download it from the run's artifacts. Pushing a
tag such as `v1.0.0` also creates a GitHub release with the exe attached. The
exe still needs the .NET 10 Desktop Runtime installed.

## What it captures

The tool checks that the focused window is mintty, Windows Terminal, or the
classic Windows console. In Windows Terminal, only the terminal content is
captured: the tab bar, title bar, and scrollbar are excluded. If the active tab has split
panes, the capture covers all of them. Any empty border on the right and
bottom is then trimmed: columns and rows that are entirely the background
colour (taken from the right-most column) are removed. The rounded window
corners at the bottom are ignored while trimming. The capture includes the visible terminal contents,
including the command you typed, and can include overlapping windows. If part
of the terminal is off-screen, that part is cropped.
