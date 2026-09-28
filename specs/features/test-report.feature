Feature: A TRX file reads as a list of tests with their outcomes

  @bullet-1
  Scenario: A passed test is read as passed
    Given a TRX file holding the passed test "TheGreeterGreetsByName" of the class "Specs.Features.GreetingFeature"
    When TestReport reads the file
    Then TestReport returns one test
    # run: printf 'Specs.Features.GreetingFeature\nTheGreeterGreetsByName'
    And the test has the class "Specs.Features.GreetingFeature" and the method "TheGreeterGreetsByName"
    And the test has the outcome passed
    And the test has an empty message

  @bullet-2
  Scenario: A failed test is read as failed with its failure message alone
    Given a TRX file holding a failed test with the failure message lines "Assert.Equal() Failure: Values differ", "Expected: 1" and "Actual:   2" and a stack trace
    When TestReport reads the file
    Then the test has the outcome failed
    # run: printf 'Assert.Equal() Failure: Values differ\nExpected: 1\nActual:   2'
    And the test message is the three lines "Assert.Equal() Failure: Values differ", "Expected: 1" and "Actual:   2"

  @bullet-3
  Scenario: A NotExecuted test is read as skipped
    Given a TRX file holding the test "GreetsInFrench" of the class "Specs.Unit.GreeterTests" with the outcome "NotExecuted"
    When TestReport reads the file
    Then the test has the outcome skipped

  @bullet-4
  Scenario: An outcome other than Passed and NotExecuted is read as failed
    Given a TRX file holding a test with the outcome "Timeout"
    When TestReport reads the file
    Then the test has the outcome failed

  @bullet-5
  Scenario: Two example rows of one outline are two tests
    Given a TRX file holding two results of the method "TheGreeterGreetsWho" of the class "Specs.Features.GreetingFeature", one passed and one failed, named with their arguments
    When TestReport reads the file
    Then TestReport returns two tests
    # run: printf 'TheGreeterGreetsWho\nTheGreeterGreetsWho'
    And the methods of the tests are "TheGreeterGreetsWho" and "TheGreeterGreetsWho"
    And one test has the outcome passed and one test has the outcome failed

  @bullet-6
  Scenario: A test run with no results is an empty list
    Given a TRX file holding a test run with no results
    When TestReport reads the file
    Then TestReport returns an empty list

  @bullet-7
  Scenario: A file that is not XML is refused
    # run: printf 'This is not a test report.'
    Given a TRX file holding the one line "This is not a test report."
    When TestReport reads the file
    Then TestReport throws a TestReportException
    And the exception message names the path of the file
    And the exception message holds the message of the parser
