using FFmpeg.AutoGen;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace NBSDecoder.Converters;

public unsafe class NBSToPngFramesConverter
{
    private NewBasis _nbs;
    private string _outputDir;

    public NBSToPngFramesConverter(NewBasis newBasisFile)
    {
        _nbs = newBasisFile;
    }

    public void ConvertToFrames(string outputDir)
    {
        _outputDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"Converting {_nbs.FrameCount} to png (may take a while!)");
        _nbs.IterateFrames(OnFrame);
    }

    private void OnFrame(int keyIndex, nint framePtr, nint alphaFramePtr)
    {
        AVFrame* incomingBaseFrame = (AVFrame*)framePtr;
        AVFrame* incomingAlphaFrame = (AVFrame*)alphaFramePtr;

        Console.WriteLine($"Frame #{keyIndex}");
        try
        {
            using Image<Rgba32> frameImg = GetFrameImage(incomingBaseFrame, incomingAlphaFrame);
            string fileName = $"{keyIndex}.png";

            frameImg.Save(Path.Combine(_outputDir, fileName));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to get frame from ffmpeg");
        }
    }

    /// <summary>
    /// Gets an image out of a frame (and optionally alpha frame)
    /// </summary>
    /// <param name="baseFrame"></param>
    /// <param name="alphaFrame"></param>
    /// <returns></returns>
    static Image<Rgba32> GetFrameImage(AVFrame* baseFrame, AVFrame* alphaFrame = null)
    {
        byte[] rgb = new byte[baseFrame->width * baseFrame->height * 4];
        ConvertYUV420ToRGBA(baseFrame, alphaFrame, rgb);

        Image<Rgba32> img = Image.LoadPixelData<Rgba32>(rgb, baseFrame->width, baseFrame->height);
        return img;
    }

    /// <summary>
    /// Convert a VP9 YUV frame to RGB
    /// </summary>
    /// <param name="baseFrame">Base color frame</param>
    /// <param name="alphaFrame">Alpha VP9 frame</param>
    /// <param name="outputRgba">Output RGBA buffer</param>
    static unsafe void ConvertYUV420ToRGBA(AVFrame* baseFrame, AVFrame* alphaFrame, byte[] outputRgba)
    {
        int width = baseFrame->width;
        int height = baseFrame->height;

        byte* yPlane = (byte*)(baseFrame->data)[0];
        byte* uPlane = (byte*)(baseFrame->data)[1];
        byte* vPlane = (byte*)(baseFrame->data)[2];
        byte* aPlane = alphaFrame is not null ? (byte*)(alphaFrame->data)[0] : null;

        int yStride = baseFrame->linesize[0];
        int uStride = baseFrame->linesize[1];
        int vStride = baseFrame->linesize[2];
        int aStride = alphaFrame is not null ? alphaFrame->linesize[0] : -1;

        int index = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int yVal = yPlane[y * yStride + x];
                int uVal = uPlane[(y / 2) * uStride + (x / 2)];
                int vVal = vPlane[(y / 2) * vStride + (x / 2)];
                int aVal = aPlane is not null ? aPlane[y * aStride + x] : -1;  // full resolution

                int c = yVal - 16;
                int d = uVal - 128;
                int e = vVal - 128;

                // Compute RGB
                int r = (298 * c + 409 * e + 128) >> 8;
                int g = (298 * c - 100 * d - 208 * e + 128) >> 8;
                int b = (298 * c + 516 * d + 128) >> 8;

                // Clamp to 0-255
                r = r < 0 ? 0 : r > 255 ? 255 : r;
                g = g < 0 ? 0 : g > 255 ? 255 : g;
                b = b < 0 ? 0 : b > 255 ? 255 : b;

                // RGBA order (4 bytes per pixel)
                outputRgba[index++] = (byte)r;     // R
                outputRgba[index++] = (byte)g;     // G
                outputRgba[index++] = (byte)b;     // B
                outputRgba[index++] = aPlane is not null ? (byte)aVal : (byte)0xFF; // A
            }
        }
    }
}
