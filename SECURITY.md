# Security Policy

Security is important to the MeshSMO project. We appreciate responsible security research and reports that help us keep MeshSMO.Sensors and its users safe.

Please **do not publicly disclose a suspected vulnerability before it has been investigated and, where appropriate, fixed**.

## Supported Versions

MeshSMO.Sensors is under active development. Security fixes are provided for the current version of the project.

| Version                               | Supported |
| ------------------------------------- | :-------: |
| Current `master` branch               |     ✅     |
| Latest published release              |     ✅     |
| Older releases and tags               |     ❌     |
| Forks and modified third-party builds |     ❌     |

When a security fix is released, users should upgrade to the latest available version or container image.

## Reporting a Vulnerability

**Do not report security vulnerabilities through public GitHub Issues, Discussions, pull requests, or other public channels.**

The preferred reporting method is GitHub Private Vulnerability Reporting:

**https://github.com/MeshSMO/MeshSMO.Sensors/security/advisories/new**

If private vulnerability reporting is unavailable, contact a MeshSMO maintainer through GitHub and request a private communication channel. Do not include vulnerability details in the initial public message.

### What to Include

A useful vulnerability report should contain as much of the following information as possible:

* a clear description of the vulnerability;
* the affected component and version or commit;
* the security impact and realistic attack scenario;
* prerequisites required to exploit the issue;
* detailed reproduction steps;
* a minimal proof of concept, if available;
* relevant logs, requests, responses, stack traces, or screenshots;
* suggested mitigations or fixes, if known;
* whether the vulnerability has already been disclosed to anyone else.

Please avoid including real credentials, private keys, access tokens, passwords, or sensitive data belonging to third parties.

## Response Process

After receiving a report, maintainers will make a reasonable effort to:

1. acknowledge the report;
2. reproduce and assess the issue;
3. determine its severity and affected versions;
4. develop and validate a fix or mitigation where appropriate;
5. coordinate disclosure with the reporter;
6. publish an updated release or container image when required.

Complex issues may require additional investigation, particularly when they involve hardware, radio communication, third-party firmware, or deployment-specific infrastructure.

We ask reporters to keep vulnerability details confidential until a fix has been released or disclosure has otherwise been coordinated with the maintainers.

## Scope

Security issues in the following MeshSMO.Sensors components are generally in scope:

* `MeshSMO.Sensors.Web` and the public HTTP API;
* the React/TypeScript frontend;
* `MeshSMO.Sensors.Gateway`;
* communication between the Gateway and MeshCore devices;
* authentication or authorization performed by this project;
* sensor registry processing;
* SQLite outbox processing;
* PostgreSQL persistence and database access performed by the application;
* `MeshSMO.Sensors.DbMigrator`;
* forecasting endpoints and related server-side processing;
* Docker images and deployment configuration maintained in this repository;
* GitHub Actions workflows where a vulnerability could compromise the repository, published artifacts, or users;
* handling of secrets, credentials, tokens, connection strings, or other sensitive configuration.

Examples of vulnerabilities we are particularly interested in include:

* remote code execution;
* authentication or authorization bypass;
* SQL, command, or other injection vulnerabilities;
* server-side request forgery (SSRF);
* path traversal or arbitrary file access;
* cross-site scripting (XSS) with meaningful security impact;
* cross-site request forgery (CSRF) where a privileged action can be performed;
* exposure of credentials, secrets, tokens, or connection strings;
* unauthorized modification or injection of sensor measurements;
* unauthorized access to Gateway or MeshCore administrative functions;
* privilege escalation;
* container or CI/CD vulnerabilities that result in code execution or supply-chain compromise;
* vulnerabilities that allow crossing an intended trust boundary.

## Security Model and Trust Boundaries

Understanding the intended trust model is important when evaluating potential vulnerabilities.

### Public telemetry

MeshSMO.Sensors is designed to publish sensor information through a public-facing service.

Sensor measurements and metadata intentionally exposed through the public API or frontend should therefore **not be assumed to be confidential**.

Access to data that is not intended to be public, or the ability to modify, forge, or delete measurements without authorization, may still constitute a security vulnerability.

### Gateway

`MeshSMO.Sensors.Gateway` is a trusted component.

It may have access to:

* a MeshCore device;
* MeshCore administrative credentials;
* USB or serial interfaces;
* locally stored telemetry;
* credentials required to upload data to the server.

An attacker who already has administrative access to the host running the Gateway is generally considered to be inside this trust boundary.

However, a vulnerability that allows an external or less-privileged attacker to cross this boundary is in scope.

### MeshCore device access

The Gateway can communicate with a MeshCoreTel repeater using HTTPS or a local serial connection.

Device administrative passwords, authentication tokens, and equivalent credentials must be treated as secrets.

The MeshCore device management interface is intended to remain on a trusted network and should not be exposed directly to the public Internet.

### TLS certificate validation

MeshSMO.Sensors supports an explicit configuration option that allows an invalid or self-signed MeshCore HTTPS certificate to be accepted.

This option exists to support devices using their normal self-signed certificates and is intended **only for trusted local networks**.

The documented security consequences of deliberately enabling this option are not, by themselves, considered a vulnerability.

A vulnerability that bypasses certificate verification when the option is disabled, or otherwise defeats the configured trust model, is in scope.

### Database and infrastructure

PostgreSQL, container hosts, reverse proxies, operating systems, and the network on which MeshSMO.Sensors is deployed are expected to be appropriately secured by the operator.

Possession of valid database administrator credentials, root access, Docker daemon access, or equivalent infrastructure-level privileges is considered administrative access.

A vulnerability in MeshSMO.Sensors that allows an attacker to obtain such privileges or access those systems without the expected credentials remains in scope.

## Third-Party Components

MeshSMO.Sensors integrates with external projects and dependencies, including MeshCore-compatible firmware and third-party .NET and JavaScript packages.

A vulnerability that exists exclusively in an upstream dependency or firmware project should normally be reported to that project's maintainers.

However, please report the issue to MeshSMO as well if:

* MeshSMO.Sensors introduces the vulnerable behavior;
* the vulnerability results from the way MeshSMO.Sensors integrates with the dependency;
* MeshSMO.Sensors bypasses or weakens an upstream security control;
* a vulnerable dependency creates a directly exploitable condition in a supported MeshSMO.Sensors deployment; or
* it is unclear which project is responsible.

We would rather receive a report and redirect it than miss a genuine vulnerability.

## Generally Out of Scope

The following are generally not considered vulnerabilities unless they result in a meaningful violation of a documented security boundary:

* vulnerabilities that require prior root or administrator access to the affected host;
* physical attacks against a device already controlled by the attacker;
* radio jamming, RF interference, or general LoRa availability limitations;
* denial of service caused solely by loss of radio connectivity;
* attacks that require possession of credentials already granting equivalent access;
* intentionally public sensor telemetry;
* missing security headers without a demonstrated security impact;
* self-XSS;
* clickjacking on pages without sensitive actions;
* rate-limiting observations without a practical security impact;
* automated scanner reports without evidence of exploitability;
* dependency version reports without a demonstrated impact on MeshSMO.Sensors;
* theoretical issues without a realistic attack path;
* social engineering or phishing;
* attacks against services or infrastructure not operated or maintained by MeshSMO;
* issues caused solely by an operator deliberately exposing an interface documented as private or trusted;
* behavior caused by explicitly disabling TLS certificate validation in a deployment where the documentation requires a trusted local network.

This list is not exhaustive. If you are unsure whether an issue is in scope, reporting it privately is preferred.

## Testing Guidelines

Security research must be performed responsibly.

Please:

* test against systems and devices you own or have explicit permission to test;
* prefer local development environments where possible;
* minimize access to real user or production data;
* stop testing if you gain unexpected access to sensitive information;
* avoid destructive actions;
* avoid disrupting the public MeshSMO service, LoRa network, or other users;
* do not perform denial-of-service or resource-exhaustion attacks against production infrastructure;
* do not attempt to obtain credentials through social engineering;
* do not persist access after demonstrating the vulnerability;
* do not modify or delete data beyond what is necessary to demonstrate impact.

If a proof of concept requires potentially disruptive testing, describe the proposed test privately before performing it against infrastructure operated by MeshSMO.

## Secrets and Credential Exposure

If you discover a credential, token, password, private key, connection string, or other secret that appears to belong to MeshSMO:

1. do not use it beyond what is strictly necessary to determine that it is valid or sensitive;
2. do not include it in a public Issue, commit, pull request, or discussion;
3. report it privately as soon as possible;
4. include where the secret was found, but avoid unnecessarily reproducing its full value.

Accidentally committed credentials should be considered compromised and rotated even if the commit is later removed from Git history.

## Coordinated Disclosure

We welcome coordinated disclosure.

Please allow maintainers a reasonable opportunity to investigate and remediate a vulnerability before publishing technical details.

When appropriate, a security fix may include:

* a patched release;
* updated container images;
* configuration changes or mitigations;
* a GitHub Security Advisory;
* vulnerability attribution or credit to the reporter.

If you would like to be credited publicly, include the preferred name or GitHub username in your report. Anonymous reports are also welcome.

## Good-Faith Research

We appreciate security research performed in good faith and with the goal of improving the security of MeshSMO.Sensors and its users.

Researchers are expected to:

* respect privacy;
* avoid unnecessary harm;
* operate only on systems they are authorized to test;
* minimize collection or retention of sensitive data;
* report discovered vulnerabilities privately;
* cooperate on reasonable disclosure timelines.

Thank you for helping keep MeshSMO secure.
