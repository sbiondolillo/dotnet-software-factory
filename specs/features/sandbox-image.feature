Feature: Sandbox image

  Background:
    Given a checkout directory whose global.json holds the sdk.version "10.0.100"

  @bullet-1
  Scenario: The image is looked up by its tag
    Given docker image inspect exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    # run: printf 'image\ninspect\nfactory-sandbox:test'
    Then the first command run is docker with the arguments "image", "inspect" and "factory-sandbox:test" in that order

  @bullet-2
  Scenario: An existing image runs no other command
    Given docker image inspect exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    Then SandboxImage runs no other command
    And the operation completes

  @bullet-3
  Scenario: A missing image is built with the caller's tag
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    # run: printf 'build\n--tag\nfactory-sandbox:test'
    Then the second command run is docker with the first argument "build" and arguments holding "--tag" and then "factory-sandbox:test"

  @bullet-4
  Scenario: The build reads the Dockerfile under the given directory
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    # run: printf -- '--file'
    Then the build arguments hold "--file" and then the path of sandbox/Dockerfile under the given directory

  @bullet-5
  Scenario: The build receives the SDK version from global.json
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    # run: printf -- '--build-arg\nDOTNET_SDK_VERSION=10.0.100'
    Then the build arguments hold "--build-arg" and then "DOTNET_SDK_VERSION=10.0.100"

  @bullet-6
  Scenario: A successful build completes the operation
    Given docker image inspect exits with the code 1
    And docker build exits with the code 0
    When a caller asks for the image with the tag "factory-sandbox:test"
    Then the operation completes

  @bullet-7
  Scenario: A failed build throws with the build's standard error
    Given docker image inspect exits with the code 1
    And docker build exits with a code other than 0 and writes a reason to standard error
    When a caller asks for the image with the tag "factory-sandbox:test"
    Then the operation throws a SandboxImageException
    And the exception message holds the standard error of the build
