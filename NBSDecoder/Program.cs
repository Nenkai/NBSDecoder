using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using static NBSDecoder.vp9_imports;

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
        Console.WriteLine($"NOTE: This tool is unfinished, expect issues with videos containing alpha.");

        if (Directory.Exists(args[0]))
        {
            foreach (var file in Directory.EnumerateFiles(args[0], "*.nbs", SearchOption.TopDirectoryOnly))
                ProcessFile(file);
        }
        else if (File.Exists(args[0]))
            ProcessFile(args[0]);
        else
        {
            Console.WriteLine($"Usage: NBSDecoder <input nbs file/folder containing nbs files>");
        }
    }

    static void ProcessFile(string path)
    {
        Console.WriteLine($"Opening {path}...");

        var nbs = NewBasis.Open(path);
        nbs.DumpInfo();
        nint baseCodecContext = CreateCodecContext();
        nint alphaCodecContext = CreateCodecContext();

        for (int i = 0; i < nbs.ClipCount; i++)
        {
            ClipInfo clip = nbs.Clips[i];
            string clipName = nbs.ClipNames[i];

            int count = 0;

            for (int j = (int)0; j < nbs.KeyBaseData.Count; j++)
            {
                KeyData keyInfo = nbs.KeyBaseData[j];

                if (keyInfo.KeyIndex < clip.KeyStart || keyInfo.KeyIndex >= clip.KeyStart + clip.NumKeys)
                    continue;

                Console.WriteLine($"Processing '{clipName}' (Key {keyInfo.KeyIndex})");

                AVFrame* frame = GetFrame(keyInfo, baseCodecContext, count);
                AVFrame* alpha = j < nbs.KeyAlphaData.Count ? GetFrame(nbs.KeyAlphaData[j], alphaCodecContext, count) : null; // TODO: Optimize this (double lookups?)
                count++;

                try
                {
                    using Image<Rgba32> frameImg = GetFrameImage(frame, alpha);
                    string fileName = $"{Path.GetFileNameWithoutExtension(path)}_{clipName}_key{keyInfo.KeyIndex}.png";

                    frameImg.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, fileName));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to get frame from ffmpeg");
                }

                FFMpeg_imports.av_frame_unref(frame);
                FFMpeg_imports.av_frame_free(frame);

                if (alpha is not null)
                {
                    FFMpeg_imports.av_frame_unref(alpha);
                    FFMpeg_imports.av_frame_free(alpha);
                }
            }
        }
    }

    private static nint CreateCodecContext()
    {
        nint codecContext = FFMpeg_imports.avcodec_alloc_context3(0);
        *(uint*)(codecContext + 0x4C) |= 1u; // flags - Replicated game behavior, offset is same as .so library too (with library i tested with, 2026 game)
        *(ulong*)(codecContext + 0x374) = 0x100000001; // ? ch_layout and frame_num?
        nint decoder = FFMpeg_imports.avcodec_find_decoder_by_name("vp9");

        if (FFMpeg_imports.avcodec_open2(codecContext, decoder, null) < 0)
        {
            Console.WriteLine("Failed to open vp9 codec");
            return 0;
        }

        return codecContext;
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
    /// Gets a new frame
    /// </summary>
    /// <param name="keyInfo"></param>
    /// <param name="codecContext"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    static AVFrame* GetFrame(KeyData keyInfo, nint codecContext, int count)
    {
        var packet = (AVPacket*)FFMpeg_imports.av_packet_alloc();
        FFMpeg_imports.av_new_packet(packet, (uint)keyInfo.Data.Length);
        packet->pts = count++;

        Marshal.Copy(keyInfo.Data, 0, (IntPtr)packet->data, keyInfo.Data.Length);

        fixed (byte* dataPtr = keyInfo.Data)
        {
            if (FFMpeg_imports.avcodec_send_packet(codecContext, packet) < 0)
            {
                FFMpeg_imports.av_packet_unref(packet);
                FFMpeg_imports.av_packet_free(packet);
                Console.WriteLine("Failed to send packet (invalid nbs vp9 data?)");
                return null;
            }

            AVFrame* frame = FFMpeg_imports.av_frame_alloc();

            while (true)
            {
                var res = FFMpeg_imports.avcodec_receive_frame(codecContext, frame);
                if (res != -11)
                    break;
            }

            FFMpeg_imports.av_packet_unref(packet);
            FFMpeg_imports.av_packet_free(packet);

            return frame;
        }
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

    [Obsolete("Doesn't work due to VP9 changes. This was a test with the original library.")]
    private void TryVPX(NewBasis nbs)
    {
        var iface = vpx_codec_vp9_dx();

        var codec = new vpx_codec_ctx_t();
        var cfg = new vpx_codec_dec_cfg_t()
        {
            w = (uint)0,
            h = (uint)0,

            threads = 1
        };

        if (vpx_codec_dec_init_ver(&codec, iface, &cfg, 0, VPX_DECODER_ABI_VERSION) > 0)
        {
            Console.WriteLine("Failed to initialize codec.");
        }

        if (vpx_codec_control_(&codec, 265, 0) > 0) // VP9_SET_SKIP_LOOP_FILTER
        {
            Console.WriteLine("Failed to set control.");
        }

        foreach (var key in nbs.KeyBaseData)
        {
            byte[] frameData = key.Data;
            //frameData[0] |= (0b10 << 6); - was a test to re-add the frame header (see comment in NewBasis.cs)

            int test = frameData[0] >> 6;
            int version = (frameData[0] >> 5) & 1;
            int high = (frameData[0] >> 4) & 1;
            int profile = (high << 1) + version;
            if (profile == 3)
            {
                int xd = (frameData[0] >> 3) & 1;
            }

            int showExistingFrame = (frameData[0] >> 2) & 1;

            fixed (byte* p = frameData)
            {

                if (vpx_codec_decode(&codec, p, (uint)frameData.Length, 0, 0) > 0)
                {
                    string err = Marshal.PtrToStringAnsi(vpx_codec_error(&codec));
                    string detail = Marshal.PtrToStringAnsi(vpx_codec_error_detail(&codec));

                    Console.WriteLine($"Failed to decode frame.");
                }
            }
        }

        Console.WriteLine("Hello, World!");
    }
}