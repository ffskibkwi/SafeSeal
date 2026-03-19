# Questions For User Input (Assumptions Applied)

1. Watermark text entry UX
- Question: Should empty watermark lines be allowed as intentional blank separators, or should empty lines be auto-removed?
- Assumption used now: Empty lines are allowed and rendered as blank line spacing.

2. Tint controls
- Question: Do you want to keep the fixed Fluent tint palette, or switch to a full color picker?
- Assumption used now: Kept fixed tint presets (`Blue`, `Slate`, `Crimson`, `Forest`).

3. Build reliability
- Question: Should CI/build scripts pin to single-node build (`dotnet build -m:1`) to avoid intermittent WPF temporary assembly file locks?
- Assumption used now: Verification uses single-node solution builds.