using System.IO;
namespace BookStitch.Services;

/// <summary>Resolves managed paths; rebases only a proven previous project root and existing target.</summary>
public static class ProjectManagedPathService
{
    public static string Resolve(string root,string path,string managedFolder,IEnumerable<string>? previousRoots=null)
    {
        if(string.IsNullOrWhiteSpace(path))throw new IOException("Empty managed path.");
        var normalized=path.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar);
        if(normalized.Split(Path.DirectorySeparatorChar).Contains(".."))throw new ProjectPathSafetyException("Managed path traversal is prohibited.");
        var boundary=Path.GetFullPath(Path.Combine(root,managedFolder)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var full=Path.GetFullPath(Path.IsPathRooted(normalized)?normalized:Path.Combine(root,normalized));
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        if(!full.StartsWith(boundary,comparison))
        {
            var matches=new HashSet<string>(AtomicJsonStore.PathComparer);
            foreach(var oldRoot in previousRoots??[])
            {
                if(string.IsNullOrWhiteSpace(oldRoot)||!Path.IsPathRooted(oldRoot))continue;
                var oldBoundary=Path.GetFullPath(Path.Combine(oldRoot,managedFolder)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!full.StartsWith(oldBoundary,comparison))continue;
                var candidate=Path.GetFullPath(Path.Combine(boundary,full[oldBoundary.Length..]));
                if(candidate.StartsWith(boundary,comparison))
                {
                    SafeFileSystemEnumeration.EnsureNoReparseAncestors(candidate);
                    if(File.Exists(candidate))matches.Add(candidate);
                }
            }
            // Older manifests may already contain the new root while their track paths still
            // contain the previous root. Accept only one existing, exact managed suffix.
            if(matches.Count==0 && Path.IsPathRooted(normalized) &&
                !full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,comparison))
            {
                var marker=Path.DirectorySeparatorChar+managedFolder+Path.DirectorySeparatorChar;
                for(var offset=full.IndexOf(marker,comparison);offset>=0;offset=full.IndexOf(marker,offset+marker.Length,comparison))
                {
                    var candidate=Path.GetFullPath(Path.Combine(boundary,full[(offset+marker.Length)..]));
                    if(candidate.StartsWith(boundary,comparison))
                    {
                        SafeFileSystemEnumeration.EnsureNoReparseAncestors(candidate);
                        if(File.Exists(candidate))matches.Add(candidate);
                    }
                }
            }
            if(matches.Count!=1)
            {
                var projectBoundary=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(Path.IsPathRooted(normalized)&&!full.StartsWith(projectBoundary,comparison))
                    throw new ProjectStaleManagedPathException("No unique local file resolves the stale managed path: "+path);
                throw new ProjectPathSafetyException("Managed path escapes its permitted directory: "+path);
            }
            full=matches.Single();
        }
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(full);
        return full;
    }
}

/// <summary>Stored external path evidence that cannot be uniquely resolved inside the current project.</summary>
public sealed class ProjectStaleManagedPathException(string message) : IOException(message);
