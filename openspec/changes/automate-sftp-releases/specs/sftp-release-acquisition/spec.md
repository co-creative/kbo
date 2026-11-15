## Purpose

Acquire KBO extracts securely and automatically for the existing release pipeline.

## ADDED Requirements

### Requirement: Scheduled acquisition
The system SHALL use a configurable daily build-job schedule to check the KBO SFTP directory for new extracts. Changing the schedule SHALL NOT require a code change.

#### Scenario: Newer Full extract available
- **WHEN** a scheduled run needs a Full extract and one newer than the published extract is available
- **THEN** the system selects the highest extract number among valid Full filenames

#### Scenario: No newer extract
- **WHEN** no extract newer than the published extract is available
- **THEN** the job exits successfully without publishing another release

#### Scenario: Same date on multiple extracts
- **WHEN** multiple valid Full filenames have the same date
- **THEN** the system orders them by extract number rather than date or directory order

### Requirement: Secure SFTP connection
The system SHALL verify the independently confirmed SSH host key and SHALL retrieve the latest mapped Key Vault secret once per execution using the dedicated job identity. It SHALL validate the credential payload and secret enabled, expiry and not-before metadata before connecting. Credentials SHALL NOT appear in app configuration, source files, command arguments or logs, and the system SHALL NOT fall back to an older secret version.

#### Scenario: Host key mismatch
- **WHEN** the server presents a host key that does not match the configured trusted fingerprint
- **THEN** the job fails without downloading or publishing a release

#### Scenario: Authentication or secret retrieval failure
- **WHEN** the job cannot obtain valid credentials or authenticate
- **THEN** the job fails visibly without exposing credentials or replacing the published release

#### Scenario: Invalid secret metadata or payload
- **WHEN** the latest credential is disabled, expired, not yet valid or malformed
- **THEN** the job fails without connecting or publishing

#### Scenario: Credential rotation
- **WHEN** an operator enrolls a valid new secret version under the same name
- **THEN** the next execution reads that version without an image or configuration change

#### Scenario: Cancelled retrieval
- **WHEN** credential retrieval is cancelled
- **THEN** the job stops without connecting or publishing

### Requirement: Complete downloads only
The system SHALL keep incomplete downloads out of the release pipeline and verify completed transfers before making files available for processing.

#### Scenario: Transfer interrupted
- **WHEN** an extract download is interrupted or incomplete
- **THEN** the job fails without publishing the extract and a later run can retry it

### Requirement: Existing manual flow remains available
The system SHALL retain the ability to process a manually supplied Full archive without requiring SFTP credentials.

#### Scenario: Manual release
- **WHEN** a newer valid Full archive is supplied manually and the manual release path is invoked
- **THEN** the existing build, validation and publication flow processes it without an SFTP connection
