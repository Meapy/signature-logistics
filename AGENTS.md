# Repository checks

- Managed mod: build against the locally installed Cities: Skylines II modding toolchain and managed assemblies; those proprietary files are not included in the repository container.
- UI module: run `docker build -t fix-signatures-ui Fix-Signatures.UI` to execute its build and smoke test in the pinned Node 22 container.
