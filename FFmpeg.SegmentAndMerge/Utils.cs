using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFmpeg.AutoGen.Example
{
    public static class Utils
    {
        public static unsafe void DecodeAllFramesToImages(VideoStreamDecoder decoder, VideoFrameConverter decoderCovnerter, AVHWDeviceType HWDevice, List<AVFrame> framesBuffer, List<Bitmap> bitmaps, long framesFetchCount, bool generateFiles)
        {
            var frameNumber = 0;
            while (decoder.TryDecodeNextFrame(out var frame) && frameNumber < framesFetchCount)
            {
                var convertedFrame = decoderCovnerter.Convert(frame);

                {
                    //byte[] copiedFrame = new byte[convertedFrame.linesize[0] * convertedFrame.height];
                    //Marshal.Copy((IntPtr)convertedFrame.data[0], copiedFrame, 0, copiedFrame.Length)

                    //var bitmap = new Bitmap(convertedFrame.width, convertedFrame.height, new MemoryStream(copiedFrame));

                    //Here create the Bitmap to the know height, width and format
                    Bitmap bitmap = new Bitmap(convertedFrame.width, convertedFrame.height, PixelFormat.Format24bppRgb);

                    //Create a BitmapData and Lock all pixels to be written 
                    BitmapData bmpData = bitmap.LockBits(
                                         new Rectangle(0, 0, convertedFrame.width, convertedFrame.height),
                                         ImageLockMode.WriteOnly, bitmap.PixelFormat);

                    //Copy the data from the byte array into BitmapData.Scan0
                    var szbytes = convertedFrame.linesize[0] * convertedFrame.height;
                    Buffer.MemoryCopy((void*)convertedFrame.data[0], (void*)bmpData.Scan0, szbytes, szbytes);

                    //Marshal.Copy((IntPtr)convertedFrame.data[0], 0, bmpData.Scan0, szbytes);

                    //Unlock the pixels
                    bitmap.UnlockBits(bmpData);

                    bitmaps.Add(bitmap);

                    if (generateFiles)
                    {
                        bitmap.Save($"frame.{frameNumber:D8}.jpg", ImageFormat.Jpeg);
                    }
                }



                Console.WriteLine($"frame: {frameNumber}");

                convertedFrame.pts = frame.pts;
                framesBuffer.Add(convertedFrame);
                frameNumber++;
            }
        }


        public static unsafe void EncodeImagesToH264(H264VideoStreamEncoder vse, VideoFrameConverter encoderConverter, float speedFactor, List<AVFrame> frames, List<Bitmap> bitmaps, bool useBitmapFiles, AVFormatContext* output_format_context)
        {
            Size sourceSize;
            long firstPts = 0;
            string[] frameFiles = null;
            var first = frames.First();
            if (first.pts > 0)
                firstPts = first.pts;

            if (useBitmapFiles)
            {
                frameFiles = Directory.GetFiles(".", "frame.*.jpg").OrderBy(x => x).ToArray();
                var fistFrameImage = Image.FromFile(frameFiles.First());
                sourceSize = fistFrameImage.Size;
            }
            else
            {
                sourceSize = new Size(first.width, first.height);

            }

            // ---------------------------------------------

            var frameNumber = 0;
            int ticks = 0;


            for (int i = 0; i < frames.Count; i++)
            //foreach (var frameFile in frameFiles)
            //foreach (var frame in frames)
            //foreach (var frameBitmap in bitmaps)
            {

                var f = frames[i];
                string frameFile;
                byte[] bitmapData;

                if (useBitmapFiles)
                {
                    frameFile = frameFiles[i];
                    using (var frameImage = Image.FromFile(frameFile))
                    {
                        using (var frameBitmap = frameImage is Bitmap bitmap ? bitmap : new Bitmap(frameImage))
                        {
                            bitmapData = GetBitmapData(frameBitmap);
                        }
                    }
                }
                else
                {
                    bitmapData = GetBitmapData(bitmaps[i]);
                    bitmaps[i].Dispose();
                }

                fixed (byte* pBitmapData = bitmapData)
                {
                    var data = new byte_ptrArray8 { [0] = pBitmapData };
                    var linesize = new int_array8 { [0] = bitmapData.Length / sourceSize.Height };
                    var frame = new AVFrame
                    {
                        //data = f.data,
                        //linesize = f.linesize,
                        //height = sourceSize.Height
                        data = data,
                        linesize = linesize,
                        height = sourceSize.Height
                    };
                    var convertedFrame = encoderConverter.Convert(frame);
                    //var convertedFrame = vfc.Convert(frame);
                    //convertedFrame.pts =  frameNumber ;
                    //vse.Encode(convertedFrame, input_format_context_file, output_format_context, ticks++);
                    //frame.pts = frameNumber ;
                    if (ticks > 0)
                        convertedFrame.pts = (long)((f.pts - firstPts) * speedFactor);
                   
                    vse.Encode(convertedFrame, output_format_context, ticks++);
                }

                Console.WriteLine($"frame: {frameNumber}");
                frameNumber++;
            }

            vse.CloseAndTrailingOutput(output_format_context);

        }

        public static unsafe void ConfigureTimeStamps(AVPacket* target_packet, AVStream* target_input_stream, AVFormatContext* output_format_context)
        {
            var out_stream = output_format_context->streams[target_packet->stream_index];

            target_packet->pts = ffmpeg.av_rescale_q_rnd(target_packet->pts, target_input_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            target_packet->dts = ffmpeg.av_rescale_q_rnd(target_packet->dts, target_input_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
            target_packet->duration = ffmpeg.av_rescale_q(target_packet->duration, target_input_stream->time_base, out_stream->time_base);

            target_packet->pos = -1;

        }

        private static byte[] GetBitmapData(Bitmap frameBitmap)
        {
            var bitmapData = frameBitmap.LockBits(new Rectangle(Point.Empty, frameBitmap.Size),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                var length = bitmapData.Stride * bitmapData.Height;
                var data = new byte[length];
                Marshal.Copy(bitmapData.Scan0, data, 0, length);
                return data;
            }
            finally
            {
                frameBitmap.UnlockBits(bitmapData);
            }
        }

        public static unsafe AVFormatContext* CreateInputContextFromFile(string in_filename)
        {
            AVFormatContext* input_format_context_file;
            int ret;
            if ((ret = ffmpeg.avformat_open_input(&input_format_context_file, in_filename, null, null)) < 0)
            {
                Debug.WriteLine("Could not open input file '%s'", in_filename);
                return null;
            }
            if ((ret = ffmpeg.avformat_find_stream_info(input_format_context_file, null)) < 0)
            {
                Debug.WriteLine("Failed to retrieve input stream information");
                return null;
            }
            return input_format_context_file;

        }
        public static unsafe AVFormatContext* CreateInputRTSPContextStream(string in_url)
        {
            AVFormatContext* input_format_context_stream = null;

            input_format_context_stream = ffmpeg.avformat_alloc_context();
            //_receivedFrame = ffmpeg.av_frame_alloc();
            var pFormatContext = input_format_context_stream;
            ffmpeg.avformat_open_input(&pFormatContext, in_url, null, null).ThrowExceptionIfError();
            ffmpeg.avformat_find_stream_info(input_format_context_stream, null).ThrowExceptionIfError();
            AVCodec* codec = null;
            int _streamIndex = ffmpeg
                .av_find_best_stream(input_format_context_stream, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0)
                .ThrowExceptionIfError();

            AVCodecContext* _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            _pCodecContext->max_b_frames = 2;
            _pCodecContext->gop_size = 12;

            //if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            //    ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0)
            //        .ThrowExceptionIfError();
            ffmpeg.avcodec_parameters_to_context(_pCodecContext, input_format_context_stream->streams[_streamIndex]->codecpar);
            ffmpeg.avcodec_open2(_pCodecContext, codec, null);

            return input_format_context_stream;
        }

        public static unsafe AVFormatContext* CreateOuputContextFile(AVFormatContext* input_format_context_file, AVCodecID videocodec, Size size, long bitrate, AVRational timebase, string out_filename)
        {
            AVFormatContext* output_format_context = null;

            int ret;
            int fragmented_mp4_options = 0;

            fragmented_mp4_options = 1;

            AVOutputFormat* outFmt = ffmpeg.av_guess_format("mp4", null, null);

            ffmpeg.avformat_alloc_output_context2(&output_format_context, outFmt, null, out_filename);
            if (output_format_context == null)
            {
                Debug.WriteLine("Could not create output context\n");
                ret = -1;
                return null;
            }

            AVCodec* codec;
            if ((codec = ffmpeg.avcodec_find_encoder(outFmt->video_codec)) == null)
            {
                return null;
            }
            
            if (input_format_context_file == null)
            {

                AVStream* outStrm = ffmpeg.avformat_new_stream(output_format_context, null);
            AVCodecContext* cctx;
            if ((cctx = ffmpeg.avcodec_alloc_context3(codec)) == null)
            {
                return null;
            }

            outStrm->codecpar->codec_id = videocodec;
            outStrm->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            outStrm->codecpar->width = size.Width;
            outStrm->codecpar->height = size.Height;
            outStrm->codecpar->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            outStrm->codecpar->bit_rate = bitrate;
            outStrm->time_base = timebase;

            ffmpeg.avcodec_parameters_to_context(cctx, outStrm->codecpar);

            //cctx->max_b_frames = 2;
            //cctx->gop_size = 12;

            ffmpeg.avcodec_parameters_from_context(outStrm->codecpar, cctx);
        }
            //-----------------------------------------------
            else
        {
                var number_of_streams = input_format_context_file->nb_streams;
                var streams_list = (int*)ffmpeg.av_mallocz_array(number_of_streams, (ulong)sizeof(int*));

                if (streams_list == null)
                {
                    ret = -1;
                    return null;
                }

                int stream_index = 0;
                for (long i = 0; i < input_format_context_file->nb_streams; i++)
                {
                    AVStream* out_stream;
                    AVStream* in_stream = input_format_context_file->streams[i];
                    AVCodecParameters* in_codecpar = in_stream->codecpar;
                    if (
                        in_codecpar->codec_type != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO &&
                        in_codecpar->codec_type != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO &&
                        in_codecpar->codec_type != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        streams_list[i] = -1;
                        continue;
                    }
                    streams_list[i] = stream_index++;
                    out_stream = ffmpeg.avformat_new_stream(output_format_context, null);
                    out_stream->time_base = timebase;
                    if (out_stream == null)
                    {
                        Debug.WriteLine("Failed allocating output stream\n");
                        ret = -1;
                        return null;
                    }

                    ret = ffmpeg.avcodec_parameters_copy(out_stream->codecpar, in_codecpar);
                    if (ret < 0)
                    {
                        Debug.WriteLine("Failed to copy codec parameters\n");
                        return null;
                    }

                }
            }


            // https://ffmpeg.org/doxygen/trunk/group__lavf__misc.html#gae2645941f2dc779c307eb6314fd39f10
            ffmpeg.av_dump_format(output_format_context, 0, out_filename, 1);

            // unless it's a no file (we'll talk later about that) write to the disk (FLAG_WRITE)
            // but basically it's a way to save the file to a buffer so you can store it
            // wherever you want.
            if ((output_format_context->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ret = ffmpeg.avio_open(&output_format_context->pb, out_filename, ffmpeg.AVIO_FLAG_WRITE);
                if (ret < 0)
                {
                    Debug.WriteLine("Could not open output file '%s'", out_filename);
                    return null;
                }
            }
            AVDictionary* opts = null;

            if (fragmented_mp4_options != null)
            {
                // https://developer.mozilla.org/en-US/docs/Web/API/Media_Source_Extensions_API/Transcoding_assets_for_MSE
                //ffmpeg.av_dict_set(&opts, "movflags", "frag_keyframe+empty_moov+default_base_moof", 0);
                ffmpeg.av_dict_set(&opts, "vcodec", "libx264", 0);
            }

            output_format_context->oformat = outFmt;

            // https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html#ga18b7b10bb5b94c4842de18166bc677cb
            ret = ffmpeg.avformat_write_header(output_format_context, &opts);
            //if (ret < 0)
            //{
            //    Debug.WriteLine("Error occurred when opening output file\n");
            //    return null;
            //}

            return output_format_context;

        }

    }
}
