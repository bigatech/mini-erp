# MiniERP

.NET 10 modular API + Worker, React/TypeScript frontend, PostgreSQL and S3 object storage.

Build and test run on GitHub-hosted runners. Cloud development uses a private GitHub Codespace; Docker Desktop is optional for daily development.

```text
.github/workflows/      Build, Test, manual Deploy
.devcontainer/          GitHub Codespaces environment
src/backend/hosts/      API and Worker
src/backend/modules/    Existing business modules
src/web/MiniErp.Web/     React application
tests/backend/          Six test projects
scripts/ci/             Synthetic integration and browser smoke
deploy/docker/          Three application Dockerfiles + Nginx proxy
deploy/compose/         Existing local stack, disposable CI, cloud-dev overlay
deploy/environments/    Empty environment templates (populated files ignored)
deploy/azure/           Bicep foundation, applications, optional PostgreSQL
docs/cloud-development.md  Setup, secrets, deployment, costs and URL plan
```

See [cloud development and deployment](docs/cloud-development.md). No public production release is enabled: application authentication, company scoping, migration and recovery acceptance remain open. Existing local customer data, internal business-analysis documents and acceptance artifacts are not part of the public source transfer.
