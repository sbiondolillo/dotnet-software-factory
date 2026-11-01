namespace Specs;

/// <summary>
/// A stand-in for <c>jq</c> on a machine that lacks it, such as the sandbox image, which holds <c>node</c> and no <c>jq</c>.
/// It runs the one subset of the jq language that the lockfile step of <c>factory.yml</c> uses: <c>-r</c>, string literals,
/// <c>+</c>, <c>,</c>, <c>//</c>, <c>error(...)</c>, parentheses, and the paths <c>.a</c>, <c>["a"]</c> and <c>.a["b"].c</c>.
/// Any other syntax exits with the code 3, so a step that grows past the subset fails and never passes by accident.
/// An <c>error</c> exits with the code 5 after the outputs that came before it, as jq does.
/// </summary>
public static class JqShim
{
    private const string Script = """
        const fs = require('fs');
        const args = process.argv.slice(2);
        const raw = args.includes('-r');
        const [program, file] = args.filter((a) => a !== '-r');

        class Unsupported extends Error {}
        class JqError extends Error {}

        const tokens = [];
        function lex() {
          const lexer = /\s*(?:("(?:[^"\\]|\\.)*")|(\/\/)|([()\[\],+.])|([A-Za-z_][A-Za-z0-9_]*)|$)/y;
          for (;;) {
            const m = lexer.exec(program);
            if (m === null) throw new Unsupported('cannot read ' + program.slice(lexer.lastIndex));
            if (m[0].trim() === '' && lexer.lastIndex >= program.length) break;
            if (m[1] !== undefined) tokens.push({ t: 'str', v: JSON.parse(m[1]) });
            else tokens.push({ t: m[2] ?? m[3] ?? 'id', v: m[4] ?? m[2] ?? m[3] });
          }
        }

        let pos = 0;
        const peek = () => tokens[pos];
        const take = (t) => {
          if (peek()?.t !== t) throw new Unsupported('expected ' + t);
          return tokens[pos++];
        };

        function comma() {
          const items = [alt()];
          while (peek()?.t === ',') { pos++; items.push(alt()); }
          return items.length === 1 ? items[0] : { k: 'comma', items };
        }
        function alt() {
          const l = add();
          if (peek()?.t === '//') { pos++; return { k: 'alt', l, r: alt() }; }
          return l;
        }
        function add() {
          let l = postfix();
          while (peek()?.t === '+') { pos++; l = { k: 'add', l, r: postfix() }; }
          return l;
        }
        function postfix() {
          let node = primary();
          for (;;) {
            if (peek()?.t === '[') {
              pos++; const key = take('str').v; take(']');
              node = { k: 'index', of: node, key };
            } else if (peek()?.t === '.' && tokens[pos + 1]?.t === 'id') {
              pos++; node = { k: 'index', of: node, key: take('id').v };
            } else return node;
          }
        }
        function primary() {
          const tok = peek();
          if (tok?.t === 'str') { pos++; return { k: 'lit', v: tok.v }; }
          if (tok?.t === '(') { pos++; const inner = comma(); take(')'); return inner; }
          if (tok?.t === 'id' && tok.v === 'error') {
            pos++; take('('); const msg = alt(); take(')');
            return { k: 'error', msg };
          }
          if (tok?.t === '.') {
            pos++;
            if (peek()?.t === 'id') return { k: 'index', of: { k: 'root' }, key: take('id').v };
            return { k: 'root' };
          }
          throw new Unsupported('unexpected ' + (tok ? tok.t : 'end'));
        }

        function* run(node, input) {
          switch (node.k) {
            case 'root': yield input; break;
            case 'lit': yield node.v; break;
            case 'comma': for (const item of node.items) yield* run(item, input); break;
            case 'index':
              for (const v of run(node.of, input)) {
                if (v === null) yield null;
                else if (typeof v === 'object' && !Array.isArray(v)) yield v[node.key] ?? null;
                else throw new JqError('Cannot index ' + typeof v + ' with "' + node.key + '"');
              }
              break;
            case 'add':
              for (const l of run(node.l, input)) for (const r of run(node.r, input)) {
                if (l === null) yield r;
                else if (r === null) yield l;
                else if (typeof l === 'string' && typeof r === 'string') yield l + r;
                else throw new Unsupported('+ of other values than strings');
              }
              break;
            case 'alt': {
              let any = false;
              try {
                for (const l of run(node.l, input)) if (l !== null && l !== false) { any = true; yield l; }
              } catch (e) { if (!(e instanceof JqError)) throw e; }
              if (!any) yield* run(node.r, input);
              break;
            }
            case 'error':
              for (const m of run(node.msg, input)) throw new JqError(typeof m === 'string' ? m : JSON.stringify(m));
              break;
          }
        }

        try {
          lex();
          const ast = comma();
          if (pos < tokens.length) throw new Unsupported('unexpected ' + tokens[pos].t);
          const input = JSON.parse(fs.readFileSync(file, 'utf8'));
          for (const out of run(ast, input)) {
            fs.writeSync(1, (raw && typeof out === 'string' ? out : JSON.stringify(out)) + '\n');
          }
        } catch (e) {
          if (e instanceof Unsupported) { fs.writeSync(2, 'jq shim: unsupported: ' + e.message + '\n'); process.exit(3); }
          if (e instanceof JqError) { fs.writeSync(2, 'jq: error (at ' + file + ') : ' + e.message + '\n'); process.exit(5); }
          throw e;
        }
        """;

    /// <summary>Whether a program of that name is in a directory of the <c>PATH</c>.</summary>
    public static bool OnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, program)));

    /// <summary>Writes an executable <c>jq</c> into <paramref name="directory"/>, which runs the shim on <c>node</c>.</summary>
    public static async Task InstallAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!OnPath("node"))
        {
            throw new InvalidOperationException("neither jq nor node is on the PATH, so the lockfile step has no jq to run");
        }

        Directory.CreateDirectory(directory);
        string script = Path.Combine(directory, "jq.js");
        await File.WriteAllTextAsync(script, Script, cancellationToken).ConfigureAwait(false);
        string launcher = Path.Combine(directory, "jq");
        await File.WriteAllTextAsync(launcher, $"#!/bin/sh\nexec node '{script}' \"$@\"\n", cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
