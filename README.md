# MkvSubsEnlarger
Designed to help people with visual impairments, MkvSubsEnlarger is a cross-platform accessibility tool that increases the font size of embedded subtitles in mkv files.

## Requirements
MkvSubsEnlarger makes use of ffmpeg to extract subtitles and remux files with the modified subtitles. The ffmpeg binaries can be downloaded from https://ffmpeg.org/download.html. The ffmpeg binary must be placed in the same directory as the MkvSubsEnlarger binary for it to work.

## Usage

### Windows

#### Drag and Drop
This is the easiest way to use the app on Windows. Simply select one or more mkv files, and drop them onto the executable.

#### Interactive
Run the app by double clicking on it, and instructions will appear to add files to be processed. Drag and drop files one at a time onto the console window. Once all files have been added, click on the console window to make it the active window and press enter.

#### Command Line
`.\MkvSubsEnlarger.exe file1 [file2 [...]]`

### Unix-like systems (Linux, MacOS, etc.)

#### Interactive
This is the easiest way to use the app on Unix-like systems like Linux and MacOS. Run the app by double clicking on it, and instructions will appear to add files to be processed. Drag and drop one or more files onto the console window. Once all files have been added, click on the console window to make it the active window and press enter.

#### Command Line
`./MkvSubsEnlarger file1 [file2 [...]]`

## File formats
MkvSubsEnlarger is designed to work with mkv files with subtitles in the Advanced SubStation Alpha (.ass) format. Currently, only the first subtitle stream will be processed, and made default in the resulting file. All other subtitle streams will be discarded.

There are plans for future updates to process all subtitle streams in videos, as well as to add support for files with subtitles in the SubRip (.srt) format.
