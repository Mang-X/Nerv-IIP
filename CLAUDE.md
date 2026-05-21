# Claude Instructions

## GitHub Workflow

- When creating pull requests for this repository, use the authenticated `gh` CLI directly.
- Do not try the GitHub connector first for PR creation; it has repeatedly returned 404 here, while `gh` is already authenticated and works.
- If a PR operation fails through `gh`, report the command and error clearly instead of falling back to the connector.
