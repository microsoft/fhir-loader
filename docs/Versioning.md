# Semantic Versioning for FHIR Server

This guide gives an overview of the Semantic versioning implementation in use with this project.
To achieve semantic versioning consistently and reliably the [GitVersion](https://github.com/GitTools/GitVersion) library is used.

## Git Version

### Overview

GitVersion is a software library and build task that uses Git history to calculate the version that should be used for the current build. The following sections explain how it is configured and the commands available to assist in versioning.

### Setup

A configuration file is included in each project directory that is used to setup the version strategy and specify how versioning should be calculated against the default and other branches. Currently, all commits to main will be treated as a release, all commits to other branches (including pull requests) will be treated as pre-release (e.g. `1.2.0-my-branch+1`).

The configured GitVersion versioning strategy is [mainline development](https://gitversion.net/docs/reference/versioning-modes/mainline-development), which increments the patch version on every commit to the main branch. Our current development workflow assumes that the main branch will stage a release on every commit and every commit will automatically be released.

Here are the relevant GitVersion configuration files for this repository:

- [BulkImport](/src/FhirLoader.BulkImport/GitVersion-BulkImport.yml)

### Incrementing Versions

The pull request template automatically has text at the bottom that can be modified to change the version update strategy for each project. Add an 'x' in the brackets for your scenario like below. GitVersion will automatically honor this, given only an 'x' is added with no formatting changes.

```md
[] BulkImport breaking change
[x] BulkImport feature
[] BulkImport fix
```

### Commands
Several commands are also available during the squash-merge to allow incrementing the major/minor/patch release numbers. These are project specific to enable separate versioning per project.

For a major feature or major breaking changes, the following commands can be added to the commit message:
```
+BulkImport-semver: breaking
or
+BulkImport-semver: major
```

Smaller changes can choose to increment the minor version:
```
+BulkImport-semver: feature
or
+BulkImport-semver: minor
```

Bug fixes or patches can be incremented with:
```
+BulkImport-semver: fix
or
+BulkImport-semver: patch
```