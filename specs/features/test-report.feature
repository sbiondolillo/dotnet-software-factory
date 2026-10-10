Feature: A TRX file reads as a list of tests with their outcomes

  @bullet-1
  Scenario: A passed test is read as passed
    Given a TRX file with a passed test "TheGreeterGreetsByName" of the class "Specs.Features.GreetingFeature"
    When TestReport reads the TRX file
    Then TestReport returns one test
    And the test has the class "Specs.Features.GreetingFeature" and the method "TheGreeterGreetsByName"
    And the test has the outcome passed

  @bullet-2
  Scenario: A failed test is read as failed with its failure message alone
    Given a TRX file with a failed test "GreetsTwice" of the class "Specs.Unit.GreeterTests" with a stack trace
    And its failure message is the three lines "Assert.Equal() Failure: Values differ", "Expected: 1" and "Actual:   2"
    When TestReport reads the TRX file
    Then TestReport returns one test
    And the test has the outcome failed
    And the test message is the three lines "Assert.Equal() Failure: Values differ", "Expected: 1" and "Actual:   2"

  @bullet-3
  Scenario: A not executed test is read as skipped
    Given a TRX file with a test "GreetsInFrench" of the class "Specs.Unit.GreeterTests" with the outcome "NotExecuted"
    When TestReport reads the TRX file
    Then TestReport returns one test
    And the test has the class "Specs.Unit.GreeterTests" and the method "GreetsInFrench"
    And the test has the outcome skipped

  @bullet-4
  Scenario: A test with any other outcome is read as failed
    Given a TRX file with a test "GreetsInFrench" of the class "Specs.Unit.GreeterTests" with the outcome "Timeout"
    When TestReport reads the TRX file
    Then TestReport returns one test
    And the test has the outcome failed

  @bullet-5
  Scenario: Each example row of an outline is one test
    Given a TRX file with two results of the method "TheGreeterGreetsWho" of the class "Specs.Features.GreetingFeature", one for each example row of its outline
    And one of the results passed and the other failed
    When TestReport reads the TRX file
    Then TestReport returns two tests
    And each test has the method "TheGreeterGreetsWho"
    And one test has the outcome passed and one test has the outcome failed

  @bullet-6
  Scenario: A test run with no results is an empty list
    Given a TRX file with a test run with no results
    When TestReport reads the TRX file
    Then TestReport returns an empty list

  @bullet-7
  Scenario: A file that is not a test report is refused
    Given a TRX file that holds the one line 'This is not a test report.'
    When TestReport reads the TRX file
    Then TestReport throws a TestReportException
    And the exception message names the path of the file
    And the exception message holds the message of the parser
