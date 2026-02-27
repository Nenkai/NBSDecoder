using FFmpeg.AutoGen;

using NBSDecoder.Converters;

using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NBSDecoder;

internal unsafe class Program
{
    static unsafe void Main(string[] args)
    {
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine($"- NewBasis (.nbs) converter by Nenkai");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("- https://github.com/Nenkai");
        Console.WriteLine("- https://twitter.com/Nenkaai");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("");
        Console.WriteLine($"NOTE: This tool does not support clips containing audio yet.");

        // Requires 'full-shared' binaries
        ffmpeg.RootPath = "Binaries\\ffmpeg";
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();

        NBSConvertType convertType = args.Any(e => e == "--png") ? NBSConvertType.Png : NBSConvertType.Video;

        if (args.Length < 1)
            Console.WriteLine($"Usage: NBSDecoder <input nbs file/folder containing nbs files> [--png]");

        if (Directory.Exists(args[0]))
        {
            foreach (var file in Directory.EnumerateFiles(args[0], "*.nbs", SearchOption.TopDirectoryOnly))
            {
                Console.WriteLine($"-> {file}");

                Convert(file, convertType);
            }
        }
        else if (File.Exists(args[0]))
        {
            Convert(args[0], convertType);
        }
        else
        {
            Console.WriteLine($"ERROR: File or folder not found.");
        }
    }

    private static void Convert(string file, NBSConvertType convertType)
    {
        var nbs = NewBasis.Open(file);
        nbs.DumpInfo();

        if (nbs.AudioInfo != null)
            Console.WriteLine("TODO: File has Audio data, not yet processed");

        switch (convertType)
        {
            case NBSConvertType.Video:
                {
                    var converter = new NBSToVideoConverter(nbs);

                    string newFileName = Path.GetFileNameWithoutExtension(file) + ".mp4";
                    string dir = Path.GetDirectoryName(file)!;
                    converter.ConvertToVideo(Path.Combine(dir, newFileName));
                }
                break;
            case NBSConvertType.Png:
                {
                    var converter = new NBSToPngFramesConverter(nbs);

                    string dir = Path.GetDirectoryName(file)!;
                    string outputDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(file));
                    converter.ConvertToFrames(outputDir);
                }
                break;
        }
    }

    public enum NBSConvertType
    {
        Video,
        Png,
    }
}