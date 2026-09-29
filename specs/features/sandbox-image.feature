Feature: Sandbox image
  The sandbox image is built from the repo's Dockerfile when it is missing.

  Background:
    Given a checkout directory whose global.json holds the sdk.version "10.0.100"

  @bullet-1
  Scenario: The image is inspected by its tag first
    Given docker image inspect exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    # run: printf 'docker\nimage\ninspect\nfactory-sandbox:test'
    Then the first command runs the program "docker" with the arguments "image", "inspect" and "factory-sandbox:test" in that order

  @bullet-2
  Scenario: An existing image runs no other command
    Given docker image inspect exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    # run: printf 1
    Then the runner has run 1 command
    And the operation completes

  @bullet-3
  Scenario: A missing image is built with the caller's tag
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    # run: printf 'docker\nbuild\n--tag\nfactory-sandbox:test'
    Then the second command runs the program "docker" with the first argument "build" and the arguments "--tag" then "factory-sandbox:test"

  @bullet-4
  Scenario: The build reads the Dockerfile under the caller's directory
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    # run: printf -- '--file'
    Then the build arguments hold "--file" and then the path of sandbox/Dockerfile under the checkout directory

  @bullet-5
  Scenario: The SDK version from global.json is a build argument
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    # run: printf -- '--build-arg\nDOTNET_SDK_VERSION=10.0.100'
    Then the build arguments hold "--build-arg" and then "DOTNET_SDK_VERSION=10.0.100"

  @bullet-6
  Scenario: A successful build completes the operation
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    Then the operation completes

  @bullet-7
  Scenario: A failed build throws with the build's standard error
    Given docker image inspect exits with the code 1
    And docker build exits with a code other than 0 and writes a reason to standard error
    When SandboxImage ensures the image "factory-sandbox:test" for the checkout directory
    Then the operation throws a SandboxImageException
    And the exception message holds the standard error of the build
