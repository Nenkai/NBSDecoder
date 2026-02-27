using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

namespace NBSDecoder;

public unsafe class NBS_FFMpeg
{
    // Some nice bindings exist: FFMpeg.AutoGen
    // (binaries are in the repo: https://github.com/Ruslan-B/FFmpeg.AutoGen)
    // ^ maybe use this if someone ever figures out all the frame changes.

    const string FFMPEG_PATH = "Binaries/nbs_ffmpeg/nbsextend.dll";

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr avcodec_alloc_context3(nint a);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr avcodec_find_decoder_by_name([MarshalAs(UnmanagedType.LPStr)] string str);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint avcodec_open2(nint a, nint b, void* c);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint av_packet_alloc();

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_packet_unref(AVPacket* packet);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_packet_free(AVPacket* packet);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint av_new_packet(AVPacket* packet, uint size);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint avcodec_send_packet(nint codecContext, AVPacket* packet);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern AVFrame* av_frame_alloc();

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_frame_unref(AVFrame* frame);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern void av_frame_free(AVFrame* frame);

    [DllImport(FFMPEG_PATH, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint avcodec_receive_frame(nint codecContext, AVFrame* packet);
}

public unsafe struct AVFrame
{
    public fixed ulong data[8];
    public fixed int linesize[8];
    public byte** extended_data;
    public int width, height;
    public int nb_samples;
    public int format;
    public int key_frame;
    public int pict_type;
    public nint sample_aspect_ratio;
    public nint pts;
    public nint pkt_pts;
    public nint pkt_dts;
    public int coded_picture_number;
    public int display_picture_number;
    public int quality;
    public void* opaque;
    public fixed ulong error[8];
    public int repeat_pict;
    public int interlaced_frame;
    public int top_field_first;
    public int palette_has_changed;
    public nint reordered_opaque;
    public int sample_rate;
    public ulong channel_layout;
    public fixed ulong buf[8];
    public nint extended_buf;
    public int nb_extended_buf;
    public nint side_data;
    public int nb_side_data;
    public int flags;
    public int color_range;
    public int color_primaries;
    public int color_trc;
    public int colorspace;
    public int chroma_location;
    public nint best_effort_timestamp;
    public nint pkt_pos;
    public ulong pkt_duration;
    public nint metadata;
    public int decode_error_flags;
    public int channels;
    public int pkt_size;
    public nint qp_table_buf;
}

public unsafe struct AVPacket
{
    public nint buf;
    public nint pts;
    public nint dts;
    public byte* data;
    public int size;
    public int stream_index;
    public int flags;
    public int duration;
    public nint destruct;
    public nint priv;
    public long pos;

    public long convergence_duration;
}