# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private
[security advisory form](https://github.com/Adiie0001/TeamLocum/security/advisories/new) for this repository
and include steps to reproduce. You can expect an acknowledgement within a few days.

## Scope notes

- This is a portfolio project. No production data is stored in it, and no real patient or doctor data should ever be
  entered into a copy of it.
- The demo accounts documented in the README exist only when the app runs in the `Development` environment; the
  application refuses to start with demo data or a published demo password in any other environment.
- The demo doctors' GMC numbers are deliberately invalid placeholders and all demo addresses use `example.com`.
- Secrets (the bootstrap administrator's password and the Gemini API key) are supplied through environment variables
  or user-secrets and are never committed.
