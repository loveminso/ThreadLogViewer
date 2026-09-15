# ThreadLog Viewer

- Preserve source logs and existing user files. Viewing is read-only; export must protect the source path.
- Runtime is local and offline: no network, telemetry, login, API, or server.
- Use only synthetic data in samples, tests, and performance reports. Keep real logs in ignored `local-data/`.
- Keep UI, core logic, and tests separate and small. No database, web framework, or plugin architecture.
- Test parsing, original line mapping, filtering, encoding, and source-safe export. Report build, automated tests, GUI checks, and measured performance separately.
- Install development tools or change anything outside the project only with user approval. Never upload or push without instruction.
