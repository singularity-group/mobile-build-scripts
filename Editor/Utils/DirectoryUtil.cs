using System;
using System.IO;

public static class DirectoryUtil {
    public static void DirectoryCopy(string sourceDirPath, string destDirPath) {
        DirectoryInfo rootSource = new DirectoryInfo(sourceDirPath);

        if (!rootSource.Exists) {
            throw new DirectoryNotFoundException(
                "Source directory does not exist or could not be found: "
                + sourceDirPath);
        }

        DirectoryInfo[] sourceDirs = rootSource.GetDirectories();
        // ensure destination directory exists
        Directory.CreateDirectory(destDirPath);

        // Get the files in the directory and copy them to the new destination
        FileInfo[] files = rootSource.GetFiles();
        foreach (var file in files) {
            string temppath = Path.Combine(destDirPath, file.Name);
            file.CopyTo(temppath, true);
        }

        // copying subdirectories, and their contents to destination
        foreach (DirectoryInfo subdir in sourceDirs) {
            string subDirDestPath = Path.Combine(destDirPath, subdir.Name);
            DirectoryCopy(subdir.FullName, subDirDestPath);
        }
    }

    /// <summary>
    /// Helper method because System.IO.Path.GetRelativePath is not part of .NET framework 4.8.
    /// </summary>
    /// <returns>Path seperated by forward slashes '/', regardless of the host environment</returns>
    public static string GetRelativePath(string filespec, string folder) {
        Uri pathUri = new Uri(filespec);
        // Folders must end in a slash
        if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString())) {
            folder += Path.DirectorySeparatorChar;
        }

        Uri folderUri = new Uri(folder);

        // path separated by /
        var forwardSlashPath =
            Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString());
        return forwardSlashPath;
    }
}