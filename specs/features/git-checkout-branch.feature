Feature: The checkout of a branch that origin holds

  @bullet-2
  Scenario: Git throws GitCommandException when the checkout exits with the code 1
    Given git exits with the code 1 on the checkout of a branch
    When a caller checks out the branch "factory/a1b2c3d4" in the directory of a clone
    Then Git throws GitCommandException

  @bullet-2
  Scenario: Git checks out a branch when git exits with the code 0
    Given git exits with the code 0 on the checkout of a branch
    When a caller checks out the branch "factory/a1b2c3d4" in the directory of a clone
    Then Git throws no exception
