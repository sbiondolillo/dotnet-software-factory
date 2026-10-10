Cleanup. Add `README.md` at the root of the repo. The repo has no `README.md`, so a person who opens it learns neither what the program does nor how to run it. The program and every test stay as they are.

`README.md` holds this text:

````markdown
# Greeter

A console program that prints a greeting.

## Run it

```
dotnet run --project src/App -- Ada
```

It prints `Hello, Ada!`. With no name, it prints `Hello, world!`.

The option `--shout` prints the greeting in upper case:

```
dotnet run --project src/App -- Ada --shout
```

It prints `HELLO, ADA!`.

## Test it

```
dotnet test
```
````

## Scope

- `README.md`

## Check

```sh
echo '939cb7dd5defe1ff1c198d90d99f5e5766e240e720ac9d06a73e05f1fdbc2f47  README.md' | sha256sum -c
```

