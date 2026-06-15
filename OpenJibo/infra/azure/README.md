# Azure Infra

This folder holds the managed Azure deployment shape for Open Jibo.

Current split:

- `foundation/`
  - creates the shared Azure resources such as Key Vault, ACR, Log Analytics, and storage
  - outputs the storage connection string so the deploy script can seed Key Vault secrets after deployment
- `container-apps/`
  - deploys the Open Jibo Container Apps runtime
  - reads runtime secrets from Key Vault via managed identity

The managed deployment workflow uses the foundation template first, then publishes the image, then deploys the app template, then runs migrations and smoke checks.
