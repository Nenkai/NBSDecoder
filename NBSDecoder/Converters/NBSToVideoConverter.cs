using FFmpeg.AutoGen;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace NBSDecoder.Converters;

public unsafe class NBSToVideoConverter
{
    private NewBasis _nbs;

    private AVCodec* _codec;
    private AVCodecContext* _codecContext;
    private AVStream* _videoStream;
    private AVFormatContext* _fmtContext;

    private AVStream* _audioStream;
    private AVCodecContext* _audioCodecContext;
    private AVCodecParserContext* _mp3Parser;
    private long _audioPts = 0;

    private FFmpeg.AutoGen.AVFrame* _currentFrame;
    private FFmpeg.AutoGen.AVPacket* _currentPacket;

    public NBSToVideoConverter(NewBasis newBasisFile)
    {
        _nbs = newBasisFile;
    }

    public void ConvertToVideo(string outputPath)
    {
        Console.WriteLine("Converting video to H264");

        InitCodecsAndStreams(outputPath);

        if (_nbs.AudioInfo is not null)
            WriteAudioPackets();

        _nbs.IterateFrames(OnFrame);

        FlushVideoStream();

        // Write mp4
        var res = ffmpeg.av_write_trailer(_fmtContext);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.av_write_trailer)} errored with {res}");

        DestroyObjects();

        Console.WriteLine($"> Converted to {outputPath}.");
    }

    private void FlushVideoStream()
    {
        var res = ffmpeg.avcodec_send_frame(_codecContext, null);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avcodec_send_frame)} errored with {res}");

        while (ffmpeg.avcodec_receive_packet(_codecContext, _currentPacket) == 0)
        {
            ffmpeg.av_packet_rescale_ts(_currentPacket, _codecContext->time_base, _fmtContext->streams[_currentPacket->stream_index]->time_base);

            ffmpeg.av_interleaved_write_frame(_fmtContext, _currentPacket);
            ffmpeg.av_packet_unref(_currentPacket);
        }
    }

    private void DestroyObjects()
    {
        var res = ffmpeg.avio_close(_fmtContext->pb);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.av_write_trailer)} errored with {res}");

        fixed (FFmpeg.AutoGen.AVPacket** pPacket = &_currentPacket)
            ffmpeg.av_packet_free(pPacket);

        fixed (FFmpeg.AutoGen.AVFrame** pFrame = &_currentFrame)
            ffmpeg.av_frame_free(pFrame);

        fixed (FFmpeg.AutoGen.AVCodecContext** pcodecContext = &_codecContext)
            ffmpeg.avcodec_free_context(pcodecContext);

        if (_mp3Parser is not null)
            ffmpeg.av_parser_close(_mp3Parser);

        if (_audioCodecContext is not null)
        {
            fixed (FFmpeg.AutoGen.AVCodecContext** pAudioCodecContext = &_audioCodecContext)
                ffmpeg.avcodec_free_context(pAudioCodecContext);
        }

        ffmpeg.avformat_free_context(_fmtContext);
    }

    private void InitCodecsAndStreams(string outputPath)
    {
        _codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
        _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
        _codecContext->width = (int)_nbs.Width;
        _codecContext->height = (int)_nbs.Height;
        _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        _codecContext->time_base = new AVRational { num = 1, den = (int)_nbs.FrameRate };
        _codecContext->framerate = new AVRational { num = (int)_nbs.FrameRate, den = 1 };
        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        ffmpeg.av_opt_set(_codecContext->priv_data, "preset", "slow", 0);
        ffmpeg.av_opt_set(_codecContext->priv_data, "threads", Environment.ProcessorCount.ToString(), 0);
        ffmpeg.avcodec_open2(_codecContext, _codec, null);

        // Fmt context
        AVFormatContext* fmtCtx = null;
        var res = ffmpeg.avformat_alloc_output_context2(&fmtCtx, null, null, outputPath);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avformat_alloc_output_context2)} errored with {res}");
        _fmtContext = fmtCtx;

        // Stream stuff
        _videoStream = ffmpeg.avformat_new_stream(fmtCtx, _codec);
        res = ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _codecContext);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avcodec_parameters_from_context)} errored with {res}");

        if (_nbs.AudioInfo != null)
            CreateAudioContext();

        res = ffmpeg.avio_open(&fmtCtx->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avio_open)} errored with {res}");

        res = ffmpeg.avformat_write_header(fmtCtx, null);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avformat_write_header)} errored with {res}");

        _currentFrame = ffmpeg.av_frame_alloc();
        _currentFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        _currentFrame->width  = (int)_nbs.Width;
        _currentFrame->height = (int)_nbs.Height;

        res = ffmpeg.av_frame_get_buffer(_currentFrame, 32);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.av_frame_get_buffer)} errored with {res}");

        _currentPacket = ffmpeg.av_packet_alloc();
    }

    private void CreateAudioContext()
    {
        _audioStream = ffmpeg.avformat_new_stream(_fmtContext, null);
        _audioStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        _audioStream->codecpar->codec_id   = AVCodecID.AV_CODEC_ID_MP3;
        _audioStream->codecpar->sample_rate = (int)_nbs.AudioInfo!.SampleRate;
        _audioStream->codecpar->ch_layout   = new AVChannelLayout() { nb_channels = _nbs.AudioInfo.NumChannels };     // joint stereo
        _audioStream->time_base = new AVRational() { num = 1, den = (int)_nbs.AudioInfo.SampleRate };

        AVCodec* mp3Codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_MP3);
        _audioCodecContext = ffmpeg.avcodec_alloc_context3(mp3Codec);
        var res = ffmpeg.avcodec_open2(_audioCodecContext, mp3Codec, null);
        if (res != 0)
            throw new Exception($"{nameof(ffmpeg.avcodec_open2)} for audio context errored with {res}");

        _mp3Parser = ffmpeg.av_parser_init((int)AVCodecID.AV_CODEC_ID_MP3);
    }

    private void WriteAudioPackets()
    {
        if (_audioStream == null)
            return;

        fixed (byte* audioPtr = _nbs.AudioInfo.MP3SampleData)
        {
            byte* data = audioPtr;
            int dataSize = _nbs.AudioInfo.MP3SampleData.Length;

            while (dataSize > 0)
            {
                byte* parsedData = null;
                int parsedSize = 0;

                int len = ffmpeg.av_parser_parse2(
                    _mp3Parser,
                    _audioCodecContext,
                    &parsedData,
                    &parsedSize,
                    data,
                    dataSize,
                    ffmpeg.AV_NOPTS_VALUE,
                    ffmpeg.AV_NOPTS_VALUE,
                    0);

                if (len < 0)
                    throw new Exception("MP3 parse error");

                data += len;
                dataSize -= len;

                if (parsedSize > 0)
                {
                    FFmpeg.AutoGen.AVPacket* pkt = ffmpeg.av_packet_alloc();
                    ffmpeg.av_new_packet(pkt, parsedSize);

                    NativeMemory.Copy(parsedData, pkt->data, (nuint)parsedSize);

                    pkt->stream_index = _audioStream->index;
                    pkt->pts = _audioPts;
                    pkt->dts = _audioPts;
                    pkt->duration = 1152; // MPEG1 Layer III

                    _audioPts += 1152;

                    ffmpeg.av_interleaved_write_frame(_fmtContext, pkt);
                    ffmpeg.av_packet_free(&pkt);
                }
            }
        }
    }

    private void OnFrame(int keyIndex, nint framePtr, nint alphaFramePtr)
    {
        AVFrame* incomingBaseFrame = (AVFrame*)framePtr;
        AVFrame* incomingAlphaFrame = (AVFrame*)alphaFramePtr;

        if (ffmpeg.av_frame_make_writable(_currentFrame) != 0)
            throw new Exception($"{nameof(ffmpeg.av_frame_make_writable)} errored");

        _currentFrame->pts = keyIndex;

        byte* yPlane = (byte*)(incomingBaseFrame->data)[0];
        byte* uPlane = (byte*)(incomingBaseFrame->data)[1];
        byte* vPlane = (byte*)(incomingBaseFrame->data)[2];

        int chromaWidth = incomingBaseFrame->width / 2;
        int chromaHeight = incomingBaseFrame->height / 2;

        // Y
        for (int y = 0; y < incomingBaseFrame->height; y++)
        {
            NativeMemory.Copy(yPlane + y * incomingBaseFrame->linesize[0],
                _currentFrame->data[0] + y * _currentFrame->linesize[0], 
                (nuint)incomingBaseFrame->width);
        }

        // U/V
        for (int y = 0; y < chromaHeight; y++)
        {
            NativeMemory.Copy(uPlane + y * incomingBaseFrame->linesize[1],
                _currentFrame->data[1] + y * _currentFrame->linesize[1],
                (nuint)chromaWidth);

            NativeMemory.Copy(vPlane + y * incomingBaseFrame->linesize[2],
                _currentFrame->data[2] + y * _currentFrame->linesize[2],
                (nuint)chromaWidth);
        }

    
        int res = ffmpeg.avcodec_send_frame(_codecContext, _currentFrame);
        if (res != 0)
        {
            throw new Exception($"{nameof(ffmpeg.avcodec_send_frame)} errored with {res}");
        }

        while (true)
        {
            res = ffmpeg.avcodec_receive_packet(_codecContext, _currentPacket);

            if (res == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                break; // Need more input frames

            if (res == ffmpeg.AVERROR_EOF)
                break;

            if (res < 0)
                throw new Exception($"receive_packet failed: {res}");

            ffmpeg.av_packet_rescale_ts(_currentPacket, _codecContext->time_base, _fmtContext->streams[_currentPacket->stream_index]->time_base);
            ffmpeg.av_interleaved_write_frame(_fmtContext, _currentPacket);
            ffmpeg.av_packet_unref(_currentPacket);
        }
    }
}
