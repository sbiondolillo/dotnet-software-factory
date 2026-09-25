# A command runs with a timeout and returns its exit code and output

## What to build

The factory starts other programs, such as `git` and `docker`, and reads what they print. `CommandRunner` in `src/Core` is the one place in the library that starts a program. Its `RunAsync` method takes the program, its arguments, a working directory, extra environment variables and a timeout. It returns a `CommandResult` that holds the exit code, the standard output and the standard error.

`CommandRunner` is a concrete class, and `RunAsync` is `virtual`. Code that calls the runner is tested by mocking `RunAsync` with Moq.

The factory runs on Linux, and `sh` is on the PATH wherever the tests run.

## Acceptance criteria

- A program that exits with code 3 gives a `CommandResult` whose exit code is 3.
- The result's standard output holds all the text the program wrote to standard output.
- The result's standard error holds all the text the program wrote to standard error, and the standard output holds none of it.
- A program that writes 1 MB to standard output and 1 MB to standard error completes, and both outputs arrive whole.
- An argument that holds a space, a double quote or a `$` reaches the program as one argument, character for character.
- The program runs in the given working directory: `pwd` prints that directory.
- An extra environment variable is visible to the program with its given value.
- An extra environment variable replaces an inherited variable of the same name.
- An inherited variable that the extra variables leave out is visible to the program with its inherited value.
- A program that still runs when the timeout passes is stopped, and `RunAsync` throws a `TimeoutException` whose message names the program and the timeout.
- A program that is on no PATH makes `RunAsync` throw an exception whose message names the program.
