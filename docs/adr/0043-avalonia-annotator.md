# ADR-0043: Avalonia for the annotator

- **Status:** accepted
- **Date:** 2026-10-23
- **Stage:** 1
- **Supplements:** [ADR-0014](0014-gui-technology.md) (Blazor Server for the analysis app), [ADR-0027](0027-hand-counted-passages.md)

## Context

The annotator was an OpenCV HighGUI window operated by keys only. Counting a 30-minute recording means many hours in it, and the user wanted mouse controls next to the keys: a file list, quick buttons for class and direction, navigation and playback speed buttons, brightness and contrast, a timeline coloured by class, counts per class and direction, and a permanent input for the speedometer reading. HighGUI has none of these controls. The analysis app is a browser application (ADR-0014), but the annotator needs exact frame access and immediate key response, so it stays a desktop tool.

## Decision

- The annotator window is built with **Avalonia** (Fluent theme), in C# without XAML: the controls call the same actions as the keys.
- The annotation logic (`AnnotationModel`) and the video access (`VideoPlayer`, OpenCV) are separated from the window and covered by tests.
- Keys and command line parameters stay as they were. Only the playback speeds change to 0.5×–16× (10 steps, also for − and +). Without a video argument the window opens the recordings folder of the private repository.
- Speeds of test drives are stored in the ground truth (`.gt.csv`) like every other entry. The separate `.speed.csv` of the key-only version is still read (and continued if a video has no ground truth yet); the regression reads both.
- The tool remains a console executable, so `import` and error messages keep their console output.

## Consequences

- Positive: mouse and keys can be mixed; the timeline and counts show at a glance what a video already contains; the logic is testable.
- Negative: an additional UI framework (Avalonia packages, about 60 MB in the NuGet cache) for one tool.
- The window is cross-platform in principle; the project still targets Windows like the other tools.
