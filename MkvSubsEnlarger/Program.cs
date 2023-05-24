using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

Environment.CurrentDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;

// Check for ffmpeg
if (!CheckForFfmpeg(out var ffmpegFile, out var ffprobeFile))
    return;

// Figure out which files we're working with.
var files = GetInputFiles(args);

// Start working on each file.
foreach (var mkvFile in files)
{
    ProcessFile(mkvFile);
}

Console.WriteLine(files.Count > 0 ? "Done!" : "No files were selected. Exiting.");
Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey(true);

bool CheckForFfmpeg(out FileInfo? ffmpegFile, out FileInfo? ffprobeFile)
{
    (ffmpegFile, ffprobeFile) = Environment.OSVersion.Platform switch
    {
        PlatformID.Win32NT => (new FileInfo(@".\ffmpeg.exe"), new FileInfo(@".\ffprobe.exe")),
        PlatformID.Unix => (new FileInfo(@"./ffmpeg"), new FileInfo(@"./ffprobe")),
        _ => (null, null)
    };

    if (ffmpegFile == null || ffprobeFile == null)
    {
        Console.WriteLine("I don't know how you got here, but your operating system is not supported.");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
        return false;
    }
    else if (!ffmpegFile.Exists || !ffprobeFile.Exists)
    {
        Console.WriteLine("Ffmpeg and ffprobe are requrired for this app to function, but at least one of them was not found.");
        Console.WriteLine("Currently, ffmpeg installed through package managers or similar is not supported.");
        Console.WriteLine("Please download ffmpeg and place the executable in the same folder as this program.");
        Console.WriteLine("You can download them at https://ffmpeg.org/download.html");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(true);
        return false;
    }

    return true;
}

List<FileInfo> GetInputFiles(string[] args)
{
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
        Console.WriteLine("Which file(s) do you want to modify? (Drag one or more files onto the window and then press enter).");

        // Draging files onto the console on Unix systems other than MacOS seem to give a non-escaped file path surrounded by single quotes.
        var filePaths = Console.ReadLine()!.Split('\'', StringSplitOptions.RemoveEmptyEntries);

        foreach (var filePath in filePaths)
            files.Add(new FileInfo(filePath));

        Console.WriteLine();
    }
    else if (Environment.OSVersion.Platform == PlatformID.Win32NT)
    {
        Console.WriteLine("Which file(s) do you want to modify? (Drag one or more files one at a time onto the window and then press enter).");

        // Draging files onto the console on Windows gives a non-escaped file path surrounded by double quotes.
        var filePaths = Console.ReadLine()!.Split('"', StringSplitOptions.RemoveEmptyEntries);

        foreach (var filePath in filePaths)
            files.Add(new FileInfo(filePath));

        Console.WriteLine();
    }

    return files;
}

void ProcessFile(FileInfo mkvFile)
{
    Console.WriteLine($"Processing file: {mkvFile.FullName}");

    // Make sure file exists and is mkv file
    if (!mkvFile.Exists)
    {
        Console.WriteLine("The file doesn't exist. Skipping");
        Console.WriteLine();
        return;
    }
    else if (mkvFile.Extension != ".mkv")
    {
        Console.WriteLine("The file isn't an mkv file. Skipping");
        Console.WriteLine();
        return;
    }

    // Check stream info
    GetStreamsInfo(mkvFile, out var subtitleStreams, out var streamCounts);

    // Extract subs
    ExtractSubtitles(mkvFile, subtitleStreams);

    // Modify subs
    Console.WriteLine("Enlarging subtitles...");

    foreach (var subtitleStream in subtitleStreams)
    {
        EnlargeSubtitles(subtitleStream);
    }

    // Re-mux video
    MuxMkvFile(mkvFile, subtitleStreams, streamCounts);

    // Clean up
    Console.WriteLine("Cleaning up...");
    foreach (var subtitleStream in subtitleStreams)
    {
        subtitleStream.extractedFile.Delete();
        subtitleStream.enlargedFile.Delete();
    }

    Console.WriteLine();
}

void GetStreamsInfo(FileInfo mkvFile, out List<SubtitleStreamInfo> subtitleStreams, out Dictionary<string, int> streamCounts)
{
    var ffprobeArgs = $@"""{mkvFile.FullName}""";
    var ffprobeProcessStartInfo = new ProcessStartInfo(ffprobeFile!.FullName, ffprobeArgs) { RedirectStandardError = true };
    var ffprobeProcess = new Process() { StartInfo = ffprobeProcessStartInfo };
    ffprobeProcess.Start();

    subtitleStreams = new();
    streamCounts = new();

    var currentLine = string.Empty;
    while ((currentLine = ffprobeProcess.StandardError.ReadLine()) != null)
    {
        if (currentLine.TrimStart().StartsWith("Stream"))
        {
            var match = Regex.Match(currentLine, @"Stream #\d+:(\d+)(?:\((\w+)\))?: (?:(\w+): (\w+)) ?(?:\((default)\))? ?(?:\((forced)\))?");

            if (match.Groups[3].Value == "Subtitle")
                subtitleStreams.Add(new SubtitleStreamInfo
                {
                    streamIndex = int.Parse(match.Groups[1].Value),
                    format = match.Groups[4].Value,
                    extractedFile = new FileInfo($"{mkvFile.DirectoryName}/{Path.GetFileNameWithoutExtension(mkvFile.FullName)}.{int.Parse(match.Groups[1].Value)}.ass"),
                    enlargedFile = new FileInfo($"{mkvFile.DirectoryName}/{Path.GetFileNameWithoutExtension(mkvFile.FullName)}.{int.Parse(match.Groups[1].Value)}.large.ass")
                });

            if (!streamCounts.ContainsKey($"{match.Groups[3].Value}"))
                streamCounts.Add(match.Groups[3].Value, 1);
            else
                streamCounts[match.Groups[3].Value]++;
        }
    }
    ffprobeProcess.WaitForExit();
}

void ExtractSubtitles(FileInfo mkvFile, List<SubtitleStreamInfo> subtitleStreams)
{
    Console.WriteLine("Extracting subtitles...");

    var ffmpegExctractArgs = $@"-v warning -i ""{mkvFile.FullName}""";

    foreach (var subtitleStream in subtitleStreams)
    {
        ffmpegExctractArgs += $@" -c:s ass -map 0:{subtitleStream.streamIndex} ""{subtitleStream.extractedFile.FullName}""";
    }

    var ffmpegExtractProcessStartInfo = new ProcessStartInfo(ffmpegFile!.FullName, ffmpegExctractArgs);

    var ffmpegExtractProcess = new Process() { StartInfo = ffmpegExtractProcessStartInfo };
    ffmpegExtractProcess.Start();
    ffmpegExtractProcess.WaitForExit();
}

void EnlargeSubtitles(SubtitleStreamInfo subtitleStream)
{
    using var subtitlesStreamReader = subtitleStream.extractedFile.OpenText();
    using var enlargedSubtitlesStreamWriter = subtitleStream.enlargedFile.CreateText();

    var currentLine = string.Empty;
    while (!(currentLine = subtitlesStreamReader.ReadLine())!.StartsWith("Format:"))
        enlargedSubtitlesStreamWriter.WriteLine(currentLine);
    enlargedSubtitlesStreamWriter.WriteLine(currentLine);

    var formatOptions = currentLine.Replace("Format:", null).Split(',', StringSplitOptions.TrimEntries);

    while ((currentLine = subtitlesStreamReader.ReadLine()) != "[Events]")
    {
        if (currentLine!.StartsWith("Style:"))
        {
            if (subtitleStream.format == "ass")
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
            {
                enlargedSubtitlesStreamWriter.WriteLine("Style: Default,Arial,32,&Hffffff,&Hffffff,&H0,&H0,0,0,0,0,100,100,0,0,1,3.7,0,2,10,10,10,0");
            }
        }
        else
            enlargedSubtitlesStreamWriter.WriteLine(currentLine);
    }
    enlargedSubtitlesStreamWriter.WriteLine(currentLine);
    enlargedSubtitlesStreamWriter.Write(subtitlesStreamReader.ReadToEnd());

    enlargedSubtitlesStreamWriter.Close();
    subtitlesStreamReader.Close();
}

void MuxMkvFile(FileInfo mkvFile, List<SubtitleStreamInfo> subtitleStreams, Dictionary<string, int> streamCounts)
{
    /*
    Due to the way ffmpeg treats files with embedded cover images, mapping the streams 1:1 in the new
    file breaks things if the file has an embedded cover image. Ffmpeg treats the image as a video
    stream rather than an attachment. However, copying the stream directly from one file to another
    breaks things. To make things work correctly, the image must be de-muxed/extracted from the
    original file, and re-muxed separately into the new file. If you simply copy the stream, ffmpeg
    doesn't complain, but for some reason, only the first line of embedded subtitles will play. So,
    this abomination works around that by ignoring any embedded image streams. Unfortunately, we
    don't necessarily end up with a file where the streams are mapped 1:1, and thus we also have to
    map metadata for each type of stream separately and explicitly.
    */
    Console.WriteLine("Muxing new file...");

    var enlargedSubsMkvFile = new FileInfo($"{mkvFile.DirectoryName}/{Path.GetFileNameWithoutExtension(mkvFile.FullName)} (enlarged subs).mkv");

    var ffmpegMuxArgs = $@"-v warning -i ""{mkvFile.FullName}""";

    foreach (var subtitleStream in subtitleStreams)
        ffmpegMuxArgs += $@" -i ""{subtitleStream.enlargedFile.FullName}""";

    ffmpegMuxArgs += " -c copy -map 0:V -map 0:a";

    for (var i = 1; i <= subtitleStreams.Count; i++)
        ffmpegMuxArgs += $" -map {i}:s:0";

    ffmpegMuxArgs += " -map 0:t? -map_metadata 0 -map_metadata:s:V 0:s:V";

    if (streamCounts.TryGetValue("Audio", out var audioStreamCount))
        for (var i = 0; i < audioStreamCount; i++)
            ffmpegMuxArgs += $" -map_metadata:s:a:{i} 0:s:a:{i}";
    if (streamCounts.TryGetValue("Subtitle", out var subtitleStreamCount))
        for (var i = 0; i < subtitleStreamCount; i++)
            ffmpegMuxArgs += $" -map_metadata:s:s:{i} 0:s:s:{i}";
    if (streamCounts.TryGetValue("Attachment", out var attachmentStreamCount))
        for (var i = 0; i < attachmentStreamCount; i++)
            ffmpegMuxArgs += $" -map_metadata:s:t:{i} 0:s:t:{i}";

    ffmpegMuxArgs += $@" ""{enlargedSubsMkvFile}""";

    var ffmpegMuxProcessStartInfo = new ProcessStartInfo(ffmpegFile!.FullName, ffmpegMuxArgs);
    var ffmpegMuxProcess = new Process() { StartInfo = ffmpegMuxProcessStartInfo };
    ffmpegMuxProcess.Start();
    ffmpegMuxProcess.WaitForExit();
}

struct SubtitleStreamInfo
{
    public int streamIndex;
    public string format;
    public FileInfo extractedFile;
    public FileInfo enlargedFile;
}