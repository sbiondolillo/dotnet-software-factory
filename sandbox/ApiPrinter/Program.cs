// Prints the public API of every assembly a project built under the given
// directories: one line per public type and per public or protected member,
// sorted, so two listings diff symbol by symbol. A reframe run lists the
// base commit and each build with it, and compares the two listings.
//
// WHICH ASSEMBLIES. A project's own output has a `<name>.deps.json` beside
// `<name>.dll`; a dependency copied into another project's `bin/` has none.
// So every `*.deps.json` found under the directories names one assembly.
//
// HOW THEY LOAD. `MetadataLoadContext` reads metadata without running code,
// against the runtime this program runs on, so a workpiece's own target
// framework needs no matching runtime here. The resolver sees every `.dll`
// under the directories once, by file name, plus the runtime's own.
using System.Reflection;
using System.Runtime.InteropServices;

public static class Api
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: ApiPrinter <directory>...");
            return 2;
        }

        var roots = args.Select(Path.GetFullPath).ToList();
        var targets = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.deps.json", SearchOption.AllDirectories))
            .Select(deps => Path.ChangeExtension(deps, ".dll").Replace(".deps.dll", ".dll"))
            .Where(File.Exists)
            .Distinct()
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        if (targets.Count == 0)
        {
            Console.Error.WriteLine("no built assembly: no *.deps.json with a .dll beside it under " + string.Join(", ", roots));
            return 3;
        }

        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dll in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            paths.TryAdd(Path.GetFileName(dll), dll);
        }
        foreach (string root in roots)
        {
            foreach (string dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
            {
                paths.TryAdd(Path.GetFileName(dll), dll);
            }
        }

        var lines = new SortedSet<string>(StringComparer.Ordinal);
        using (var context = new MetadataLoadContext(new PathAssemblyResolver(paths.Values), "System.Private.CoreLib"))
        {
            foreach (string target in targets)
            {
                Assembly assembly = context.LoadFromAssemblyPath(target);
                string name = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(target);
                foreach (Type type in assembly.GetTypes().Where(IsVisible))
                {
                    lines.Add($"{name}: {Api.Describe(type)}");
                    foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (Api.Describe(member) is string line)
                        {
                            lines.Add($"{name}: {type.FullName}::{line}");
                        }
                    }
                }
            }
        }
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }
        return 0;
    }

    public static bool IsVisible(Type type) => type.IsPublic || type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;

    public static string Describe(Type type)
    {
        string kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
        string mods = string.Join(" ", new[]
        {
        type.IsAbstract && type.IsSealed ? "static" : null,
        type.IsAbstract && !type.IsSealed && !type.IsInterface ? "abstract" : null,
        type.IsSealed && !type.IsAbstract && !type.IsValueType && !type.IsEnum ? "sealed" : null,
    }.Where(m => m is not null));
        var bases = new List<string>();
        if (type.BaseType is Type b && b.FullName is not "System.Object" and not "System.ValueType" and not "System.Enum")
        {
            bases.Add(b.ToString());
        }
        bases.AddRange(type.GetInterfaces().Where(IsVisible).Select(i => i.ToString()).OrderBy(s => s, StringComparer.Ordinal));
        string inherits = bases.Count == 0 ? "" : " : " + string.Join(", ", bases);
        return $"{kind} {(mods.Length == 0 ? "" : mods + " ")}{type.FullName}{inherits}";
    }

    public static string? Describe(MemberInfo member)
    {
        switch (member)
        {
            case MethodBase method when IsVisible(method) && !method.IsSpecialName:
                return $"{(method is ConstructorInfo ? "ctor" : "method")} {Access(method)} {(method.IsStatic ? "static " : "")}{method}";
            case PropertyInfo property:
                MethodInfo?[] accessors = [property.GetMethod, property.SetMethod];
                if (!accessors.Any(a => a is not null && IsVisible(a)))
                {
                    return null;
                }
                string get = property.GetMethod is MethodInfo g && IsVisible(g) ? $" {Access(g)} get;" : "";
                string set = property.SetMethod is MethodInfo s && IsVisible(s) ? $" {Access(s)} set;" : "";
                return $"property {property.PropertyType} {property.Name} {{{get}{set} }}";
            case FieldInfo field when IsVisible(field):
                string fmods = field.IsLiteral ? "const " : (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "");
                return $"field {Access(field)} {fmods}{field.FieldType} {field.Name}";
            case EventInfo ev when ev.AddMethod is MethodInfo add && IsVisible(add):
                return $"event {Access(add)} {ev.EventHandlerType} {ev.Name}";
            default:
                return null;
        }
    }

    public static bool IsVisible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    public static bool IsVisible(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;

    public static string Access(MethodBase m) => m.IsPublic ? "public" : "protected";

    public static string Access(FieldInfo f) => f.IsPublic ? "public" : "protected";
}
