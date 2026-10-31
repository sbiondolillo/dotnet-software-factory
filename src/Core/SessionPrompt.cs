using System.Globalization;
using System.Text;

namespace Core;

/// <summary>The texts that go around each prompt of a Managed Agents session: the preamble before it, the tail of the
/// builder and the ending after it.</summary>
internal static class SessionPrompt
{
    /// <summary>The text a session's answer starts with when a command of the preamble fails.</summary>
    public const string PreambleFailed = "preamble failed:";

    /// <summary>The start of the last line of an answer, which names the files the agent copied to the outputs.</summary>
    public const string OutputsLine = "outputs:";

    /// <summary>The text of a push that GitHub refused for the token.</summary>
    public const string AuthenticationFailed = "Authentication failed";

    /// <summary>Where a session finds the dotnet SDK.</summary>
    public const string Dotnet = "/root/.dotnet/dotnet";

    /// <summary>The whole text of one message: the preamble, the prompt, the tail for a builder, and the ending.</summary>
    public static string Compose(string prompt, string repo, string branch, string baseCommit, IReadOnlyList<string> uploads, SessionRole role)
    {
        var text = new StringBuilder();
        text.Append(Preamble(repo, branch, baseCommit, uploads)).Append("\n\n## The task\n\n").Append(prompt).Append("\n\n");
        if (role is SessionRole.Builder)
        {
            text.Append(Tail(repo, branch)).Append("\n\n");
        }

        return text.Append(Ending()).ToString();
    }

    /// <summary>The commands that put the session's clone at the head of <paramref name="branch"/>, clean, before any edit.</summary>
    public static string Preamble(string repo, string branch, string baseCommit, IReadOnlyList<string> uploads)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $$"""
            ## Before the task

            Run these shell commands, in this order, before you do the task. Stop at the first command that fails. Then
            reply with the line `{{PreambleFailed}}`, the command and its output, and do not do the task.

            ```
            cd /workspace/{{repo}}
            git config user.name "dsf-repair[bot]"
            git config user.email "350419598+dsf-repair[bot]@users.noreply.github.com"
            mkdir -p .git/info
            grep -qxF '.factory/' .git/info/exclude || echo '.factory/' >> .git/info/exclude
            git fetch --depth=400 origin +refs/heads/{{branch}}:refs/remotes/origin/{{branch}} {{baseCommit}}
            git checkout {{branch}}
            git pull --ff-only origin {{branch}}
            [ -x {{Dotnet}} ] || { curl -sSL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir /root/.dotnet; }
            mkdir -p .factory /mnt/session/outputs

            """);
        foreach (string name in uploads)
        {
            text.Append(CultureInfo.InvariantCulture, $"cp /mnt/session/uploads/{name} .factory/{name}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"""
            rm -f .factory/review.json .factory/threads.json
            rm -f /mnt/session/outputs/review.json /mnt/session/outputs/threads.json
            test -z "$(git status --porcelain)"
            ```

            The last command fails when `git status --porcelain` prints a line. Then reply with `{PreambleFailed}` and
            what `git status --porcelain` prints.

            Work in `/workspace/{repo}`. Use `{Dotnet}` for each `dotnet` command. The base commit of the change is
            `{baseCommit}`.

            The host reads only the directory `/mnt/session/outputs/`. When the task tells you to write a file under
            `.factory/`, such as `.factory/plan.md`, `.factory/review.json` or `.factory/threads.json`, write it there as
            the task says. Then copy it to `/mnt/session/outputs/` with the same base name, for example
            `cp .factory/plan.md /mnt/session/outputs/plan.md`. Git ignores `.factory/`, so these files are never committed.
            """);
        return text.ToString();
    }

    /// <summary>The commands that commit the builder's work and push it to <paramref name="branch"/>.</summary>
    public static string Tail(string repo, string branch) =>
        $"""
        ## After the task

        Run these shell commands in `/workspace/{repo}` after the task, also when the task failed:

        1. When `git status --porcelain` prints a line: `git add -A && git commit -q -m "<one line that says what changed>"`.
        2. Always: `git push origin HEAD:{branch}`.

        When the push fails, put the output of the push in your final message.
        """;

    /// <summary>The message that asks the builder to push again, after the host renewed the token of the repository.</summary>
    public static string PushAgain(string repo, string branch) =>
        $"""
        The host renewed the GitHub token of the repository. Run this shell command in `/workspace/{repo}` again:

        ```
        git push origin HEAD:{branch}
        ```

        When the push fails, put the output of the push in your final message. End your final message with the same
        `{OutputsLine}` line as your last final message.
        """;

    /// <summary>The instruction for the last line of the final message.</summary>
    public static string Ending() =>
        $"""
        ## The last line

        End your final message with one line that starts with `{OutputsLine}`. After it, put the base name of each file you
        copied to `/mnt/session/outputs/` for this task, separated by spaces. With no such file, the line is `{OutputsLine}`
        alone.
        """;
}
