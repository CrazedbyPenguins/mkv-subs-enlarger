using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

Environment.CurrentDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;

/*
 *  Check for ffmpeg
 */

var ffmpegFile = Environment.OSVersion.Platform switch
{
    PlatformID.Win32NT => new FileInfo(@".\ffmpeg.exe"),
    PlatformID.Unix => new FileInfo(@"./ffmpeg"),
    _ => null
};

if (ffmpegFile == null)
{
    Console.WriteLine("I don't know how you got here, but your operating system is not supported.");
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
    return;
}
else if (!ffmpegFile.Exists)
{
    Console.WriteLine("Ffmpeg is requrired for this app to function, but was not found.");
    Console.WriteLine("Please download ffmpeg and place the executable in the same folder as this program.");
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
    return;
}

/*
 *  Figure out which files we're working with.
 */

List<FileInfo> files = new();

if (args.Length > 0)
    // If file paths have been added via command line or by dropping files onto the executable, then there should simply be one path per argument.
    foreach (var arg in args)
        files.Add(new(arg));
else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    Console.WriteLine("Which file do you want to modify? (Drag one or more files onto the window and then press enter).");

    // Dragging a file onto a console window in MacOS (at least on Catalina) gives a backslash-escaped file path with no quotes.
    // The lack of quotes makes it difficult to parse the paths for more than one file at a time.
    // Dragging multiple files (either in sequence or simultaneously) will paste each path separated with a non-escaped space.
    // This regex matches for any string of characters ending with a space not preceded by a backslash.
    var matches = Regex.Matches(Console.ReadLine()!, @".+?(?<!\\) ");

    foreach (Match match in matches)
        files.Add(new FileInfo(match.Value.Replace("\\", null).TrimEnd()));

    Console.WriteLine();
}
else if (Environment.OSVersion.Platform == PlatformID.Unix)
{
    Console.WriteLine("Which file do you want to modify? (Drag one or more files onto the window and then press enter).");

    // Draging files onto the console on Unix systems other than MacOS seem to give a non-escaped file path surrounded by single quotes.
    var filePaths = Console.ReadLine()!.Split('\'', StringSplitOptions.RemoveEmptyEntries);

    foreach (var filePath in filePaths)
        files.Add(new FileInfo(filePath));

    Console.WriteLine();
}
else if (Environment.OSVersion.Platform == PlatformID.Win32NT)
{
    Console.WriteLine("Which file do you want to modify? (Drag one or more files one at a time onto the window and then press enter).");

    // Draging files onto the console on Windows gives a non-escaped file path surrounded by double quotes.
    var filePaths = Console.ReadLine()!.Split('"', StringSplitOptions.RemoveEmptyEntries);

    foreach (var filePath in filePaths)
        files.Add(new FileInfo(filePath));

    Console.WriteLine();
}

/*
 * Start working on each file.
 */

if (files.Count == 0)
{
    Console.WriteLine("No files were selected. Exiting.");
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
    return;
}

foreach (var mkvFile in files)
{
    Console.WriteLine($"Processing file: {mkvFile.FullName}");

    // Make sure file exists and is mkv file
    if (!mkvFile.Exists)
    {
        Console.WriteLine("The file doesn't exist. Skipping");
        Console.WriteLine();
        continue;
    }
    else if(mkvFile.Extension != ".mkv")
    {
        Console.WriteLine("The file isn't an mkv file. Skipping");
        Console.WriteLine();
        continue;
    }

    var directory = mkvFile.DirectoryName;
    var fileName = Path.GetFileNameWithoutExtension(mkvFile.FullName);

    // Extract subs
    Console.WriteLine("Extracting subs...");

    var subtitlesFile = new FileInfo($"{directory}/{fileName}.ass");

    var ffmpegExctractArgs = $@"-v warning -i ""{mkvFile.FullName}"" -c:s copy ""{subtitlesFile.FullName}""";
    var ffmpegExtractProcessStartInfo = new ProcessStartInfo(ffmpegFile.FullName, ffmpegExctractArgs);
    Process.Start(ffmpegExtractProcessStartInfo).WaitForExit();

    // Modify subs
    Console.WriteLine("Enlarging subs...");

    var enlargedSubtitlesFile = new FileInfo($"{directory}/{fileName} (enlarged subs).ass");
    using var subtitlesStreamReader = subtitlesFile.OpenText();
    using var enlargedSubtitlesStreamWriter = enlargedSubtitlesFile.CreateText();

    string currentLine;

    do
    {
        currentLine = subtitlesStreamReader.ReadLine();
        enlargedSubtitlesStreamWriter.WriteLine(currentLine);
    } while (currentLine != "[V4+ Styles]");
    currentLine = subtitlesStreamReader.ReadLine();
    enlargedSubtitlesStreamWriter.WriteLine(currentLine);

    var formatOptions = currentLine.Replace("Format:", null).Split(',', StringSplitOptions.TrimEntries);

    while(currentLine != "[Events]")
    {
        currentLine = subtitlesStreamReader.ReadLine();

        if (currentLine.StartsWith("Style:"))
        {
            var styles = currentLine.Replace("Style:", null).Split(',', StringSplitOptions.TrimEntries);

            var fontSize = int.Parse(styles[Array.IndexOf(formatOptions, "Fontsize")]);
            fontSize += 20;
            styles[Array.IndexOf(formatOptions, "Fontsize")] = $"{fontSize}";

            var outline = decimal.Parse(styles[Array.IndexOf(formatOptions, "Outline")]);
            outline += 5;
            styles[Array.IndexOf(formatOptions, "Outline")] = $"{outline}";

            enlargedSubtitlesStreamWriter.WriteLine($"Style: {string.Join(',', styles)}");
        }
        else
            enlargedSubtitlesStreamWriter.WriteLine(currentLine);
    }

    enlargedSubtitlesStreamWriter.Write(subtitlesStreamReader.ReadToEnd());

    enlargedSubtitlesStreamWriter.Close();
    subtitlesStreamReader.Close();

    // Re-mux video
    Console.WriteLine("Muxing new file...");

    var enlargedSubsMkvFile = new FileInfo($"{directory}/{fileName} (enlarged subs).mkv");

    var ffmpegMuxArgs = $@"-v warning -i ""{mkvFile.FullName}"" -i ""{enlargedSubtitlesFile.FullName}"" -c:v copy -c:a copy -c:s copy -map 0:v -map 0:a -map 1:s -map 0:t -disposition:s:0 default ""{enlargedSubsMkvFile.FullName}""";
    var ffmpegMuxProcessStartInfo = new ProcessStartInfo(ffmpegFile.FullName, ffmpegMuxArgs);
    Process.Start(ffmpegMuxProcessStartInfo).WaitForExit();

    // Clean up
    Console.WriteLine("Cleaning up...");
    subtitlesFile.Delete();
    enlargedSubtitlesFile.Delete();

    Console.WriteLine();
}

Console.WriteLine("Done!");
Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey(true);