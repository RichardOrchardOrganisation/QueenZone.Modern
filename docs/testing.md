# Test-writing policy

Tests protect observable behavior. Select the smallest layer that proves the risk; avoid duplicating every case through every layer.

| Layer | Proves | Default dependency policy |
| --- | --- | --- |
| Unit | Pure calculations, validation, routing, visibility, sanitisation | No host, filesystem, network, or database |
| Integration | HTTP results, auth boundaries, service composition, persistence contracts | Deterministic fakes, samples, or disposable local providers |
| Consumer contract | Client/server schema and compatibility | Dedicated contract suite; separate from client coverage totals |
| Browser/device | Critical user journeys and platform behavior | Small deterministic PR suite; real device or emulator for native behavior |
| Provider probe | SQL types, locking, transactions, retry strategies | Opt-in safe test/mirror database; never production writes |

Tests should fail when the promised behavior is wrong. Cover happy paths plus meaningful boundaries, missing/hidden resources, rejected input, permission failures, and state transitions. For a bug fix, capture the trigger and expected result as a regression test. Avoid asserting private method structure, snapshots without a clear contract, and “does not throw” tests when there is a concrete result to assert.

Repository contract tests should define behavior once and exercise each implementation with equivalent seeds. Keep provider-specific checks separate. SQLite and in-memory providers cannot prove production SQL mappings, locks, execution strategies, or server-side ordering.

Keep test environments structurally isolated from ambient production connection strings. Fail closed if a real-data test host targets an unapproved database. Write probes must use throwaway records and guaranteed cleanup; preserve diagnostics on failures without leaving production residue.

Do not retry failed assertions. A narrowly classified infrastructure failure may have one documented recovery attempt if the original evidence is retained. Avoid fragile timing assumptions; use observable completion signals and bounded waits.

Do not write tests for every trivial reversible documentation or formatting edit. Run relevant checks and broaden only when new changes or unresolved failures justify it. Default tests run on every PR; expensive real-data and broad UI suites can run separately on a documented schedule.
