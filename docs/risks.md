# Delivery Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Docker daemon may be installed but not running | PostgreSQL integration and Compose verification cannot run | Check daemon before integration tests; report any unavailable verification explicitly |
| NuGet network access may be restricted | Restore can block implementation | Prefer stable `net10.0` packages and request network permission only if restore fails |
| Git repository ownership differs from the current user | Ordinary Git inspection is rejected | Use per-command `-c safe.directory=<repository>`; do not modify global Git settings |
| Interactive Server circuits are long-lived | A scoped `DbContext` can become stale or unsafe | Use `IDbContextFactory` and dispose a context per operation |
| Tenant filtering can be omitted accidentally | Cross-organization data disclosure | Centralize current-organization resolution, require organization predicates, and add real PostgreSQL isolation tests |
| Concurrent approval actions can race | Two reviewers may both appear successful | Add a PostgreSQL concurrency token and test conflicting reviews |
| Demo credentials can become secrets | Unsafe repository defaults | Read the demo password from configuration/environment and provide placeholders only |
| Exported user text can become a formula or invalid XML | Spreadsheet injection or corrupt files | Sanitize control characters and prefix formula-like values before Open XML output |

