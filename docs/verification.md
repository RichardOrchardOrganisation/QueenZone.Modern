# Verification and proof

Automated checks and actual UI observation provide different evidence. After UI changes, exercise the affected journeys on the intended platform and capture reviewable screenshots or recordings. Mobile proof must come from the native app on a device/emulator/simulator. Expo web is useful during development but is not native proof.

Maintain config/feature-map.json. Each feature has a stable id, source files or directory prefixes, a command/journey to exercise, and a platform. The feature-map check ensures configured UI files are mapped and entries point to existing sources. The PR verification checker requires affected feature IDs, Command:, Platform:, Result:, and a proof URL or artifacts/proof/ path. A `Not verified:` line is an honest alternative when a check could not run; it is not proof that the behavior works. The checker validates documentation shape, not screenshot contents or artifact existence. Reviewers decide whether an unverified check blocks merge.

For opt-outs, use the no-ui-verification label plus Verification-skip-reason: and explain why verification is unnecessary. Keep proof artifacts free of secrets and private user data. Store links or retained CI artifacts; generated local output need not be committed.

The generic map is the portable interface. A project with a richer model can keep its own map and checker as a reviewed local customisation of these managed files. Examples include domain-split maps validated against navigation registrations, page directives, test IDs, or device flows. QueenZone.Modern does this, because that validation depends on its frameworks. Keep the PR-evidence rules above either way.

The sample map is empty. Populate it after configuring uiPaths. Browser start commands, device IDs, test accounts, selectors, capture commands, runner labels, and proof storage are project-specific. Do not copy QueenZone’s runners or database fixtures.

The verification runner covers host-free code checks. Browser/device capture and safe real-data probes remain explicit project jobs. The PR must distinguish passed, failed, and NOT RUN checks and name every skipped requirement.
