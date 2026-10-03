# dotnet templates

- A template that ships `packages.lock.json` files gives `.template.config/template.json`
  a `sources` entry with its own `exclude` list. That list replaces the engine's
  default list whole, which drops every `*.lock.json`. Name in it every path the copy
  leaves out, `.template.config/**` included.
- A hermetic test that runs `dotnet new` or a restore sets `DOTNET_CLI_HOME` to a fresh
  directory. That directory then holds the template store `.templateengine/`, the
  package cache `.nuget/packages/` and the first-run sentinel.
