## Purpose

Produce correct immutable releases from consecutive KBO Update extracts, with recovery from missing or invalid incremental state.

## ADDED Requirements

### Requirement: Daily updates and monthly Full rebuilds
The system SHALL check daily for new extracts and perform a Full rebuild in each UTC calendar month when a newer extract is available. Between Full rebuilds it SHALL use a complete compatible Update chain, subject to the recovery requirements below.

#### Scenario: First newer extract of a month
- **WHEN** a newer extract is available and no Full rebuild has been published in the current UTC calendar month
- **THEN** the system builds and publishes a Full release

#### Scenario: No newer extract
- **WHEN** no extract newer than the current release is available
- **THEN** the system leaves the current release unchanged

### Requirement: Apply a complete update chain
The system SHALL apply the required consecutive KBO Update extracts in their defined order to a compatible baseline, including insertions and deletions, to produce a release for the selected target extract.

#### Scenario: Consecutive updates
- **WHEN** a compatible baseline and every required valid Update extract are available
- **THEN** the system applies the complete chain and prepares a release for the target extract

#### Scenario: Missing or incompatible baseline or update
- **WHEN** the baseline is absent or incompatible, or a required update is missing
- **THEN** the system builds from a suitable Full extract instead of applying an incomplete chain

### Requirement: Match Full-release behavior
An incremental release SHALL represent the same source data, company documents, code descriptions and search behavior as a Full build of the same target extract, apart from build-time metadata.

#### Scenario: Updates affect related and indexed data
- **WHEN** updates add, change or remove an enterprise, unit, address, name, contact, activity or code
- **THEN** company documents, summaries, code descriptions and search results match the corresponding Full build

### Requirement: Immutable and validated publication
The system SHALL build incremental releases and their recoverable baseline state separately from published releases, validate them before publication, and never modify a published release in place.

#### Scenario: Failed update or validation
- **WHEN** processing or validation fails
- **THEN** the job fails without switching the current release or leaving partially applied state as a usable baseline

#### Scenario: Interrupted publication
- **WHEN** a job is interrupted during publication
- **THEN** a subsequent run can identify a consistent release and baseline or safely recover through a Full rebuild

#### Scenario: Rollback
- **WHEN** an operator selects a retained previous release
- **THEN** the web app can use it without mutation, and subsequent processing does not apply updates to incompatible baseline state

### Requirement: No overlapping publishers
The system SHALL prevent concurrent release runs from modifying the same staging or publication state.

#### Scenario: Concurrent runs
- **WHEN** a second release run starts while another holds the publication lease
- **THEN** the second run does not publish or alter the first run's staging state
