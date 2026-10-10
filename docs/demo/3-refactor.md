Refactor. Move the upper-case rule of the greeting from `GreetCommand` into `Greeter`. After the change, `Greeter` in `src/Core/Greeter.cs` holds the public static method `Greet(string name, bool shout)`, which returns the greeting of `name`, in upper case when `shout` is `true`. The action of `GreetCommand` gets its text from that method and prints it as it is.

The program keeps its behaviour, and every test stays as it is. The tests call `Greeter.Greet(string name)` and `GreetCommand.Create()` with no parameter, so both stay public with the signatures they have on `main`.

