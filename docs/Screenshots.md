# Screenshot checklist

The README and guides contain visible, labeled screenshot placeholders, not broken image links. Replace a placeholder when the real screenshot is available; the HTML comment identifies its slot. Keep screenshots in `docs/assets/screenshots/` when adding them.

## Shots to supply

1. **`overview.png`**: the main window after analyzing a signed sample application. Show File information, the certificate chain, and a prepared rule. Used in the README and user guide.
2. **`startup-modes.png`**: the first-start choice between portable and per-user installation, including the installed-only **Add to Explorer context menu** checkbox. Used in the installation guide.
3. **`certificate-chain.png`**: the signer/intermediate/root list with completeness status and export buttons. Used in the user guide.
4. **`certificate-selection.png`**: the selected certificate DN, the analyzed chain dropdown, uploaded certificate option, and Publisher/Certificate authority choice. Used in the user guide.
5. **`intune-policy.png`**: a sanitized optional sign-in panel and Elevation Rules policy dropdown, including + Add new. Two separate screenshots are fine if clearer. Used in the user guide.
6. **`updates.png`**: the update panel with version, mode, startup-check checkbox, and an available update. Used in the installation guide.

Prefer readable crops at normal Windows scaling. A matching dark-mode overview is welcome as an additional screenshot, not a requirement.

## Replace a slot

For the README overview, replace its blockquote and associated `SCREENSHOT` comment with:

```markdown
![EPMRuleCreator showing file details, a certificate chain, and a prepared elevation rule](docs/assets/screenshots/overview.png)
```

For pages inside `docs/`, the relative image path starts with `assets/screenshots/` instead of `docs/assets/screenshots/`. Add a short caption explaining what the reader should notice. Only add the Markdown image link once the file exists.

## Before publishing

- Use a test file and test tenant, not production policies or customer data.
- Redact UPNs, tenant/client/policy IDs, personal paths, and sensitive certificate subjects or internal application details.
- Inspect Logs, dropdowns, tooltips, and the account panel for data that needs redaction.
- Never include tokens, cache files, secrets, or private keys.
- Include the actual current app UI, not an invented mockup.
