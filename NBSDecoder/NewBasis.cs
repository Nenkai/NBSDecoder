using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Syroot.BinaryData;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NBSDecoder;

public class NewBasis
{
    // This is some kind of animation format that houses vp9 frames for each keyframe.

    // They have 2 VP9 decoders:
    // - NBSDecoderInner, google's vp9 library; usually statically linked into their game
    // - NBSDecoderOuter, ffmpeg as a side dynamic library with vp9 support only (libnbsextend.so or nbsextend.dll)
    //
    // They made some changes to vp9 in BOTH cases that makes the original library or ffmpeg incompatible.
    //
    // Namely:
    // - there is no frame header magic (0b10), so first byte is always 0x32 instead of i.e 0xA2
    // - read_bitdepth_colorspace_sampling doesn't seem to read the extra 3 bits for subsampling_x/subsampling_y either.
    //   essentially, after sync code, and color_space read, there is no 3 bit read (1 (subsampling_x) + 1 (subsampling_x) + 1 (reserved))
    // - (not relevant to video frame format) some way to batch (?) serialize frames, with new vpx_codec_control values (270+)
    //   - 270 = set serial frame
    //   - 273 = serial frame allocator
    //   - 274 = serial frame deallocator
    // - decoder eventually calls SerialFrameBuffers, which calls vpx::SerialerMgr::GetSerialer.
    // 
    // - ... and more to figure out.
    //
    // For now we use theirs.
    // It would be wise to maybe diff nbsextend.dll with a own compiled ffmpeg, since the compile arguments are included in the executable.
    // Would make diffing easier.


    public byte UnkType { get; set; }

    public List<ClipInfo> Clips { get; set; } = [];
    public List<KeyClipInfo> KeyBaseData { get; set; } = [];
    public List<byte[]> Frames { get; set; } = [];

    public AlphaInfo? AlphaInfo { get; set; }
    public AudioInfo? AudioInfo { get; set; }

    public byte ClipCount { get; set; }
    public byte Field_0x01 { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint FrameCount { get; set; }
    public uint FrameRate { get; set; }
    public ulong BaseColorDataVP9StartOffset { get; set; }
    public ulong BaseColorDataVP9Size { get; set; }
    public string[] ClipNames { get; set; }

    public float Duration => (float)FrameCount / FrameRate;

    public static NewBasis Open(string file)
    {
        var nbs = new NewBasis();

        using var fs = File.OpenRead(file);
        nbs.OpenImpl(fs);
        return nbs;
    }

    public void DumpInfo()
    {
        Console.WriteLine($"--------------------------------");
        Console.WriteLine($"            NBS INFO            ");
        Console.WriteLine($"--------------------------------");
        Console.WriteLine($"> Clip Count: {ClipCount}");
        Console.WriteLine($"> Dimensions: {Width}x{Height}");
        Console.WriteLine($"> Frames: {FrameCount}");
        Console.WriteLine($"> Frame Rate: {FrameRate}");
        Console.WriteLine($"> Total Duration: {Duration:F2}s");
        Console.WriteLine($"--------------------------------");
        Console.WriteLine($"> BaseColorDataOffset: 0x{BaseColorDataVP9StartOffset:X}");
        Console.WriteLine($"> BaseColorDataSize: 0x{BaseColorDataVP9Size:X}");
        Console.WriteLine($"--------------------------------");
        Console.WriteLine("Clips:");
        for (int i = 0; i < Clips.Count; i++)
        {
            ClipInfo clipInfo = Clips[i];
            float startSec = (float)clipInfo.KeyStart / FrameRate;
            float endSec = (float)((float)clipInfo.KeyStart + clipInfo.NumKeys) / FrameRate;
            Console.WriteLine($"> Clip '{ClipNames[i]}' [{clipInfo.KeyStart}->{clipInfo.KeyStart+clipInfo.NumKeys-1}] ({startSec}s -> {endSec:F2}s)");
        }

        Console.WriteLine($"--------------------------------");
        if (AlphaInfo is not null)
        {
            Console.WriteLine("AlphaInfo:");
            Console.WriteLine($"> DataOffset: 0x{AlphaInfo.DataOffset:X}");
            Console.WriteLine($"> DataSize: 0x{AlphaInfo.DataSize:X}");
            Console.WriteLine($"--------------------------------");
        }

        if (AudioInfo is not null)
        {
            Console.WriteLine("AudioInfo:");
            Console.WriteLine($"> Unk: {AudioInfo.Unk}");
            Console.WriteLine($"> Channels: {AudioInfo.NumChannels}");
            Console.WriteLine($"> Sample Count: {AudioInfo.SampleCount}");
            Console.WriteLine($"> Sample Rate: {AudioInfo.SampleRate}");
            Console.WriteLine($"> Data Size: {AudioInfo.MP3SampleData.Length:X}");
            Console.WriteLine($"--------------------------------");
        }
    }

    private void OpenImpl(Stream stream)
    {
        Console.WriteLine("Reading header...");
        BinaryStream bs = new BinaryStream(stream);
        byte version = bs.Read1Byte();
        byte unkType = bs.Read1Byte();

        Console.WriteLine($"Version: {version}");
        Console.WriteLine($"Type: {unkType}");

        byte numSections = bs.Read1Byte();
        Console.WriteLine($"Num sections: {numSections}");

        List<NbsSection> sections = new(numSections);
        for (int i = 0; i < numSections; i++)
            sections.Add(new NbsSection((NbsSectionType)bs.Read1Byte(), bs.ReadInt32()));

        foreach (var section in sections)
        {
            bs.Position = section.Offset;
            switch (section.Type)
            {
                case NbsSectionType.BaseInfo:
                    ReadBaseInfo(bs);
                    break;
                case NbsSectionType.KeyInfo:
                    ReadKeys(bs);
                    break;
                case NbsSectionType.BaseColorData:
                    ReadBaseColorData(bs);
                    break;
                case NbsSectionType.AudioInfo:
                    ReadAudioInfo(bs);
                    break;
                case NbsSectionType.AlphaInfo:
                    ReadAlphaInfo(bs);
                    break;
                case NbsSectionType.KeyInfo6:
                    Console.WriteLine("TODO: KeyInfo6 section");
                    break;
                case NbsSectionType.AlphaInfo8:
                    Console.WriteLine("TODO: AlphaInfo8 section");
                    break;
                default:
                    Console.WriteLine($"WARN: Unsupported section type {section.Type}, skipping");
                    break;
            }
        }

    }

    private void ReadAudioInfo(BinaryStream bs)
    {
        AudioInfo = new();
        AudioInfo.Unk = bs.Read1Byte();
        AudioInfo.NumChannels = bs.Read1Byte();
        AudioInfo.SampleRate = bs.ReadUInt32();
        AudioInfo.SampleCount = bs.ReadUInt64();
        uint dataSize = bs.ReadUInt32();
        AudioInfo.MP3SampleData = bs.ReadBytes((int)dataSize);
        return;
    }

    private void ReadBaseColorData(BinaryStream bs)
    {
        for (int j = 0; j < FrameCount; j++)
        {
            uint size = bs.ReadUInt32();
            byte[] frameData = bs.ReadBytes((int)size);
            Frames.Add(frameData);
        }
    }

    private void ReadBaseInfo(BinaryStream bs)
    {
        ClipCount = bs.Read1Byte();
        Field_0x01 = bs.Read1Byte();
        Width = bs.ReadUInt32();
        Height = bs.ReadUInt32();
        FrameCount = bs.ReadUInt32();
        FrameRate = bs.ReadUInt32();
        BaseColorDataVP9StartOffset = bs.ReadUInt32();
        BaseColorDataVP9Size = bs.ReadUInt32();

        uint namesLength = bs.ReadUInt32();
        byte[] nameBuffer = bs.ReadBytes((int)namesLength);
        string str = Encoding.UTF8.GetString(nameBuffer);
        ClipNames = str.Split(" ");

        long basePos = bs.Position;
        for (int i = 0; i < ClipCount; i++)
        {
            bs.Position = basePos + (i * 0x10);
            var clip = new ClipInfo()
            {
                KeyStart = bs.ReadUInt32(),
                NumKeys = bs.ReadUInt32(),
                BaseColorDataOffset = bs.ReadUInt32(),
                BaseColorDataSize = bs.ReadUInt32(),
            };

            Clips.Add(clip);
        }

        for (int i = 0; i < ClipNames.Length; i++)
        {
            if (i < Clips.Count)
                Clips[i].Name = ClipNames[i];
        }
    }

    private void ReadAlphaInfo(BinaryStream bs)
    {
        byte version = bs.Read1Byte();
        if (version == 1)
        {
            sbyte unk = bs.ReadSByte();
            return;
        }

        AlphaInfo = new();

        // ClipAlphaData
        for (int i = 0; i < ClipCount; i++)
        {
            ClipInfo clip = Clips[i];
            clip.AlphaDataOffset = bs.ReadUInt32();
            clip.AlphaDataSize = bs.ReadUInt32();
            // TODO
        }

        for (int i = 0; i < KeyBaseData.Count; i++)
        {
            uint keyIndex = bs.ReadUInt32();
            uint frameDataOffset = bs.ReadUInt32();
            AlphaInfo.KeyAlphaData.Add(new KeyClipInfo(keyIndex, frameDataOffset));
        }

        AlphaInfo.DataOffset = bs.ReadUInt32();
        AlphaInfo.DataSize = bs.ReadUInt32();

        bs.Position = AlphaInfo.DataOffset;
        for (int i = 0; i < ClipCount; i++)
        {
            ClipInfo clip = Clips[i];
            bs.Position = clip.AlphaDataOffset;

            for (int j = 0; j < clip.NumKeys; j++)
            {
                uint size = bs.ReadUInt32();
                byte[] frameData = bs.ReadBytes((int)size);
                AlphaInfo.Frames.Add(frameData);
            }
        }
    }

    private void ReadKeys(BinaryStream bs)
    {
        int numKeys = bs.ReadInt32();

        for (int i = 0; i < numKeys; i++)
        {
            uint keyIndex = bs.ReadUInt32();
            uint frameDataOffset = bs.ReadUInt32();
            KeyBaseData.Add(new KeyClipInfo(keyIndex, frameDataOffset));
        }        
    }

    public delegate void OnFrameDelegate(int keyIndex, nint baseFrame, nint alphaFrame);
    public unsafe void IterateFrames(OnFrameDelegate onFrameCallback)
    {
        nint baseCodecContext = CreateCodecContext();
        nint alphaCodecContext = CreateCodecContext();

        int count = 0;

        for (int j = 0; j < Frames.Count; j++)
        {
            byte[] frameData = Frames[j];
            AVFrame* frame;
            AVFrame* alpha;
            count++;

            try
            {
                frame = GetFrame(frameData, baseCodecContext, count, "base color");
                alpha = AlphaInfo is not null ? GetFrame(AlphaInfo.Frames[j], alphaCodecContext, count, "alpha") : null;
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get frame {j} from nbs ffmpeg");
                break;
            }

            onFrameCallback(j, (nint)frame, (nint)alpha);

            NBS_FFMpeg.av_frame_unref(frame);
            NBS_FFMpeg.av_frame_free(frame);

            if (alpha is not null)
            {
                NBS_FFMpeg.av_frame_unref(alpha);
                NBS_FFMpeg.av_frame_free(alpha);
            }
        }
    }

    private unsafe static nint CreateCodecContext()
    {
        nint codecContext = NBS_FFMpeg.avcodec_alloc_context3(0);
        *(uint*)(codecContext + 0x4C) |= 1u; // flags - Replicated game behavior, offset is same as .so library too (with library i tested with, 2026 game)
        *(ulong*)(codecContext + 0x374) = 0x100000001; // ? ch_layout and frame_num?
        nint decoder = NBS_FFMpeg.avcodec_find_decoder_by_name("vp9");

        if (NBS_FFMpeg.avcodec_open2(codecContext, decoder, null) < 0)
        {
            Console.WriteLine("Failed to open vp9 codec");
            return 0;
        }

        return codecContext;
    }

    /// <summary>
    /// Gets a new frame
    /// </summary>
    /// <param name="keyInfo"></param>
    /// <param name="codecContext"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    static unsafe AVFrame* GetFrame(byte[] data, nint codecContext, int count, string debugName)
    {
        var packet = (AVPacket*)NBS_FFMpeg.av_packet_alloc();
        NBS_FFMpeg.av_new_packet(packet, (uint)data.Length);
        packet->pts = count++;

        Marshal.Copy(data, 0, (IntPtr)packet->data, data.Length);

        fixed (byte* dataPtr = data)
        {
            if (NBS_FFMpeg.avcodec_send_packet(codecContext, packet) != 0)
            {
                NBS_FFMpeg.av_packet_unref(packet);
                NBS_FFMpeg.av_packet_free(packet);
                Console.WriteLine($"Failed to send {debugName} packet (invalid or dummy nbs vp9 data? error could be ignorable)");
                return null;
            }

            AVFrame* frame = NBS_FFMpeg.av_frame_alloc();

            while (true)
            {
                var res = NBS_FFMpeg.avcodec_receive_frame(codecContext, frame);
                if (res != -11)
                    break;
            }

            NBS_FFMpeg.av_packet_unref(packet);
            NBS_FFMpeg.av_packet_free(packet);

            return frame;
        }
    }

}

public class AlphaInfo
{
    public List<KeyClipInfo> KeyAlphaData { get; set; } = [];
    public List<byte[]> Frames { get; set; } = [];
    public uint DataOffset { get; set; }
    public uint DataSize { get; set; }
}

public class AudioInfo
{
    public byte Unk { get; set; }
    public byte NumChannels { get; set; }
    public uint SampleRate { get; set; }
    public ulong SampleCount { get; set; }
    public byte[] MP3SampleData { get; set; }
}
public class ClipInfo
{
    public uint KeyStart { get; set; }
    public uint NumKeys { get; set; }
    public uint BaseColorDataOffset { get; set; }
    public uint BaseColorDataSize { get; set; }
    public uint AlphaDataOffset { get; set; }
    public uint AlphaDataSize { get; set; }

    public string Name { get; set; }
}

public record NbsSection(NbsSectionType Type, int Offset);
public record KeyClipInfo(uint KeyIndex, uint FrameDataStartOffset);

public enum NbsSectionType : byte
{
    BaseInfo = 0,
    KeyInfo = 1,
    BaseColorData = 2, // Data read by nbs::BaseColorDecoder (inherits from nbs::VP9Decoder)
    AudioInfo = 3,
    AlphaInfo = 4, // Data read by nbs::AlphaDecoder (inherits from nbs::VP9Decoder)
    KeyInfo6 = 6,
    AlphaInfo8 = 8,
}

