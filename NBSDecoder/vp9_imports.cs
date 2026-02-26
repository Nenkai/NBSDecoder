using System;
using System.Collections.Generic;
using System.Text;

namespace NBSDecoder;

// For bindings: https://github.com/ShiftMediaProject/libvpx
// Used 1.15.1.

public unsafe class vp9_imports
{
    const string VPXPATH = @"C:\Users\nenkai\source\repos\vpxtest2\msvc\bin\x64\vpxd.dll";

    public const int VPX_IMAGE_ABI_VERSION = 5;
    public const int VPX_CODEC_ABI_VERSION = 4 + VPX_IMAGE_ABI_VERSION;
    public const int VPX_DECODER_ABI_VERSION = 3 + VPX_CODEC_ABI_VERSION;

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern IntPtr vpx_codec_error(vpx_codec_ctx_t* ctx);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern IntPtr vpx_codec_error_detail(vpx_codec_ctx_t* ctx);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern int vpx_codec_decode(vpx_codec_ctx_t* ctx, byte* data,
                             uint data_sz, nint user_priv,
                             long deadline);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern int vpx_codec_get_frame(vpx_codec_ctx_t* ctx, nint iter);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern int vpx_codec_control_(vpx_codec_ctx_t* ctx, int ctrl_id, nint data);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern vpx_codec_iface_t* vpx_codec_vp9_dx();

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern int vpx_codec_dec_init_ver(vpx_codec_ctx_t* ctx,
                                   vpx_codec_iface_t* iface,
                                   vpx_codec_dec_cfg_t* cfg,
                                   uint flags, int ver);

    [System.Runtime.InteropServices.DllImport(VPXPATH, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    public static extern int vpx_codec_destroy(vpx_codec_ctx_t* ctx);

    public struct vpx_codec_iface_t { }

    public unsafe struct vpx_codec_ctx_t
    {
        byte* name;
        vpx_codec_iface_t* iface;
        int err;
        byte* err_detail;
        int init_flags;
        vpx_codec_dec_cfg_t* config; // anonymous, union config { dec enc raw }
        nint priv;
    };

    public unsafe struct vpx_codec_dec_cfg_t
    {
        public uint threads;
        public uint w;
        public uint h;
    };
}
