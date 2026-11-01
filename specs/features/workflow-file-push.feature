Feature: The factory delivers a run whose change is to a workflow file

  Background:
    Given the work tree "/work" is the clone of the run "a1b2c3d4" of the repo "dotnet-software-factory" of the owner "sbiondolillo"
    And the base branch is "main" and the issue 407 has the title "Reword one comment line of factory.yml, throwaway proof for #406 step 4"
    And git answers each command with the exit code 0

  Scenario: Deliver opens the pull request for a run whose push carries a change to a workflow file
    Given the run changed the file ".github/workflows/factory.yml"
    And GitHub answers the open of the pull request with the number 408 and the web address "https://github.com/sbiondolillo/dotnet-software-factory/pull/408"
    When a caller delivers the run with the token "token"
    Then git pushed the branch "factory/a1b2c3d4" from "/work" to origin
    And Deliver gives back the pull request with the number 408 and the web address "https://github.com/sbiondolillo/dotnet-software-factory/pull/408"

  Scenario: A push that GitHub refuses for the workflows permission throws GitCommandException with the refusal and opens no pull request
    Given the run changed the file ".github/workflows/factory.yml"
    And git answers the push with the exit code 1 and the standard error "refusing to allow a GitHub App to create or update workflow `.github/workflows/factory.yml` without `workflows` permission"
    When a caller delivers the run with the token "token"
    Then Deliver throws GitCommandException whose message holds "without `workflows` permission"
    And Deliver sent no request to GitHub
