# Versioning and compatibility

- The SDK follows [semantic versioning](https://semver.org/). Its version (1.0.0) is independent of the API version.
- **Patch** releases fix bugs. **Minor** releases add operations, models or members (generated from a newer contract) and
  options. **Major** releases change or remove public signatures.
- The API is versioned in its path (`/api/v1`) and only makes additive changes within a version. Every answer carries
  `X-Api-Version`.
- The package version is in `src/Transynq.Sdk/Transynq.Sdk.csproj` and `SdkInfo.Version`; `SpecInfo.Sha256` and `SpecInfo.ApiVersion` say which contract the code was generated from.
- Models ignore unknown properties and codes are open strings, so an older SDK keeps working against a newer API v1.
- Supported runtimes: .NET 8 and .NET 10 (the package targets `net8.0` and `net10.0`). A target framework leaves the package in a
  minor release after its Microsoft end of support.

See the [changelog](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/CHANGELOG.md) for the changes of each release.
