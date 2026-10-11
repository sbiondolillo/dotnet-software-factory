Cleanup. The factory requests a Copilot code review of each pull request it watches. The run command in `.github/workflows/factory.yml` and in `.github/workflows/review-feedback.yml` ends in `--pass-env EXAMPLE_API_KEY`, so the factory requests no review. Add `--copilot-review on` after `--pass-env EXAMPLE_API_KEY` on that line in both files. Every other line of both files stays as it is.

## Scope

- `.github/workflows/factory.yml`
- `.github/workflows/review-feedback.yml`

## Check

```sh
grep -q -- '--pass-env EXAMPLE_API_KEY --copilot-review on$' .github/workflows/factory.yml && grep -q -- '--pass-env EXAMPLE_API_KEY --copilot-review on$' .github/workflows/review-feedback.yml
```
