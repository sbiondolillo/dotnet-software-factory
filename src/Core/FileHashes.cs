using System.Security.Cryptography;
using System.Text;

namespace Core;

/// <summary>The hashes of the entries of one work tree. The walk enters each directory that is no symbolic link. A symbolic link is
/// one entry, with the hash of its target text, as git stores a link, so no read goes through a link and the walk enters no linked
/// directory. An entry of 0 bytes gets the hash of empty bytes and is never opened: .NET on Linux gives a named pipe the attributes
/// of a regular file, and a pipe, a socket and a device node each have the length 0. Each other entry is a regular file, read as a
/// stream up to the length that the walk saw, so the memory use is flat for any size, and a device that a process swaps in after
/// the walk saw a file is read no further than that length.</summary>
public sealed class FileHashes(string workTree)
{
    private static readonly string EmptyHash = Convert.ToHexStringLower(SHA256.HashData(ReadOnlySpan<byte>.Empty));

    /// <summary>Gives each entry whose relative path, with '/', <paramref name="include"/> keeps, mapped to the lowercase hex
    /// SHA-256 of its bytes, of its link target text for a symbolic link, or of empty bytes for an entry of 0 bytes. A directory
    /// is no entry, and <paramref name="include"/> sees no directory path.</summary>
    public Dictionary<string, string> Read(Func<string, bool> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(new DirectoryInfo(workTree), "", include, result);
        return result;
    }

    private static void Walk(DirectoryInfo directory, string prefix, Func<string, bool> include, Dictionary<string, string> result)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            string relative = prefix + entry.Name;
            if (entry.LinkTarget is string target)
            {
                if (include(relative))
                {
                    result[relative] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target)));
                }
            }
            else if (entry is DirectoryInfo child)
            {
                Walk(child, relative + "/", include, result);
            }
            else if (entry is FileInfo file && include(relative))
            {
                result[relative] = file.Length is 0 ? EmptyHash : Hash(file);
            }
        }
    }

    private static string Hash(FileInfo file)
    {
        using FileStream stream = file.OpenRead();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long left = file.Length;
        while (left > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read is 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            left -= read;
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
