using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFmpeg.SegmentAndMerge
{
    public class SegmentMerger
    {
        public static unsafe void MergeSegments(string inputFilePatern, string outputFilePath, TimeSpan startTime, TimeSpan recordDuration, AVHWDeviceType HWDevice, long defaultFps = 30, bool ignoreAudio = false)
        {
            var frameFiles = Directory.GetFiles(".", inputFilePatern).OrderBy(x => x).ToArray();
            var inputFiles = new List<IntPtr>();

            int videoStreamIndex = 0;
            int audioStreamIndex = 1;

            foreach (var fname in frameFiles)
            {
                var input_format_context_file = Utils.CreateInputContextFromFile(fname);
                if (input_format_context_file == null)
                {
                    continue;
                }

                inputFiles.Add((IntPtr)input_format_context_file);
            }

            var first_input_format_context = (AVFormatContext*)inputFiles.First();


            Size sourceSize = new Size(first_input_format_context->streams[videoStreamIndex]->codec->width, first_input_format_context->streams[videoStreamIndex]->codec->height);

            //var templateInputFileName = "template.mp4";
            //var template_input_file = Utils.CreateInputContextFromFile(templateInputFileName);

            var videoCodec = first_input_format_context->streams[videoStreamIndex]->codec;
            AVRational videoTimebase = videoCodec->time_base;

            var audioCodec = first_input_format_context->streams[audioStreamIndex]->codec;
            AVRational audioTimebase = audioCodec->time_base;

            AVFormatContext* template_properties_video = first_input_format_context;
            //AVFormatContext* template_properties_video = null;


            AVFormatContext* output_format_context_file = Utils.CreateOuputContextFile(template_properties_video,
                                                                                        videoCodec->codec_id,
                                                                                        sourceSize,
                                                                                        videoCodec->bit_rate,
                                                                                        videoTimebase,
                                                                                        audioCodec->codec_id,
                                                                                        audioCodec->bit_rate,
                                                                                        audioCodec->time_base,
                                                                                        outputFilePath);

            var firstNbStreams = first_input_format_context->nb_streams;

            TimeSpan endTime = startTime + recordDuration;
            long[] perStreamTick = new long[firstNbStreams];
            var perStreamPrevPts = new long?[firstNbStreams];
            int[] perStreamIndex = new int[firstNbStreams];

            TimeSpan accumulatedDurationFromFirstVideo = default(TimeSpan);

            int k = 0;
            foreach (var input in inputFiles)
            {
                var input_format_context = (AVFormatContext*)input;
                ffmpeg.av_dump_format(input_format_context, 0, frameFiles[k++], 0);
                // warning hardcoded parameter

                //var timebase = input_format_context->streams[videoStreamIndex]->time_base;
                var vtimeBase = input_format_context->streams[videoStreamIndex]->time_base;
                var secs = input_format_context->streams[videoStreamIndex]->duration * vtimeBase.num / (float)vtimeBase.den;
                var segmentDuration = TimeSpan.FromSeconds(secs);
                double fps = defaultFps;

                if (accumulatedDurationFromFirstVideo > endTime)
                {
                    break;
                }
                else //if (accumulatedDurationFromFirstVideo + segmentDuration >= startTime)
                {
                    AVPacket packet;
                    int ret;
                    bool endSegment = false;

                    var currentVideoEllapsed = default(TimeSpan);
                    do
                    {
                        ret = ffmpeg.av_read_frame(input_format_context, &packet);

                        if (ret < 0) // probably final frame
                        {
                            //var buffer = new byte[2048];
                            //fixed(byte* p= &buffer[0])
                            //{ 
                            //    ffmpeg.av_make_error_string(p, 2048, ret);
                            //}

                            //var errstr = System.Text.ASCIIEncoding.ASCII.GetString(buffer);
                            break;
                        }

                        int pkgStreamIndex = packet.stream_index;
                        //int pkgStreamIndex = 0;


                        perStreamIndex[pkgStreamIndex]++;

                        var inStream = input_format_context->streams[packet.stream_index];
                        var outStream = output_format_context_file->streams[packet.stream_index];
                        if (!perStreamPrevPts[pkgStreamIndex].HasValue)
                            perStreamPrevPts[pkgStreamIndex] = packet.pts;

                        var prevPts = packet.pts;
                        packet.duration = packet.pts - perStreamPrevPts[pkgStreamIndex].Value;
                        perStreamPrevPts[pkgStreamIndex] = packet.pts;

                        packet.pts = ffmpeg.av_rescale_q_rnd(packet.pts, inStream->time_base, outStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                        packet.dts = ffmpeg.av_rescale_q_rnd(packet.dts, inStream->time_base, outStream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                        packet.duration = ffmpeg.av_rescale_q(packet.duration, inStream->time_base, outStream->time_base);

                        var durationInSeconds = TimeSpan.FromSeconds((float)packet.duration * inStream->time_base.num / (float)inStream->time_base.den);

                        if (pkgStreamIndex == videoStreamIndex)
                        {
                            currentVideoEllapsed += durationInSeconds;
                            accumulatedDurationFromFirstVideo += durationInSeconds;
                        }

                        // check skip ignore audio
                        if (ignoreAudio && packet.stream_index != videoStreamIndex)
                            continue;

                        // skip if previous to record
                        if (accumulatedDurationFromFirstVideo < startTime)
                        {
                            endSegment = false;
                            continue;
                        }

                        // stop after recording
                        if (packet.stream_index == videoStreamIndex && accumulatedDurationFromFirstVideo > endTime)
                            break;



                        //if (packet.pts < 0)
                        //    packet.pts = 0;

                        //if (perStreamPrevPts[pkgStreamIndex] == null)
                        //{
                        //    perStreamPrevPts[pkgStreamIndex] = packet.pts;
                        //}


                        //var duration = 2*(packet.pts - perStreamPrevPts[pkgStreamIndex].Value);


                        //perStreamPrevPts[pkgStreamIndex] = packet.pts;

                        //packet.pts = perStreamTick[pkgStreamIndex];
                        //packet.dts = perStreamTick[pkgStreamIndex];

                        //perStreamTick[pkgStreamIndex] += duration;



                        packet.pos = -1;
                        ret = ffmpeg.av_interleaved_write_frame(output_format_context_file, &packet);
                        ffmpeg.av_packet_unref(&packet);
                        endSegment = ret != 0 && ret != -22;

                    }
                    while (!endSegment);

                    Debug.WriteLine("video consumed");
                }
                //else
                //{
                //    if (!perStreamPrevPts[videoStreamIndex].HasValue)
                //        perStreamPrevPts[endTime] = packet.pts;

                //    accumulatedDurationFromFirstVideo += segmentDuration;
                //}

                //

            }

            ffmpeg.av_write_trailer(output_format_context_file);
            ffmpeg.avformat_free_context(output_format_context_file);
        }


        private static void ConfigureHWDecoder(out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            Console.WriteLine("Use hardware acceleration for decoding?[n]");
            var key = Console.ReadLine();
            var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();

            if (key == "y")
            {
                Console.WriteLine("Select hardware decoder:");
                var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                var number = 0;

                while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    Console.WriteLine($"{++number}. {type}");
                    availableHWDecoders.Add(number, type);
                }

                if (availableHWDecoders.Count == 0)
                {
                    Console.WriteLine("Your system have no hardware decoders.");
                    HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                    return;
                }

                var decoderNumber = availableHWDecoders
                    .SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
                if (decoderNumber == 0)
                    decoderNumber = availableHWDecoders.First().Key;
                Console.WriteLine($"Selected [{decoderNumber}]");
                int.TryParse(Console.ReadLine(), out var inputDecoderNumber);
                availableHWDecoders.TryGetValue(inputDecoderNumber == 0 ? decoderNumber : inputDecoderNumber,
                    out HWtype);
            }
        }

        private static unsafe void SetupLogging()
        {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            // do not convert to local function
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(line);
                Console.ResetColor();
            };

            ffmpeg.av_log_set_callback(logCallback);
        }

        private static unsafe void DecodeAllFramesToImages(VideoStreamDecoder decoder, VideoFrameConverter decoderCovnerter, AVHWDeviceType HWDevice, string url, List<AVFrame> framesBuffer, List<Bitmap> bitmaps, int framesFetchCount, bool generateFiles)
        {
            var frameNumber = 0;
            while (decoder.TryDecodeNextFrame(out var frame) && frameNumber < framesFetchCount)
            {
                var convertedFrame = decoderCovnerter.Convert(frame);

                if (generateFiles)
                {
                    using (var bitmap = new Bitmap(convertedFrame.width,
                                convertedFrame.height,
                                convertedFrame.linesize[0],
                                PixelFormat.Format24bppRgb,
                                (IntPtr)convertedFrame.data[0]))
                    {
                        bitmap.Save($"frame.{frameNumber:D8}.jpg", ImageFormat.Jpeg);
                    }
                }
                else
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
                }

                Console.WriteLine($"frame: {frameNumber}");

                convertedFrame.pts = frame.pts;
                framesBuffer.Add(convertedFrame);
                frameNumber++;
            }
        }

        private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
        {
            return hWDevice switch
            {
                AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
                AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
                AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
                AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
                AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
                AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
                AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
                AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
                AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
                AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
                AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
                _ => AVPixelFormat.AV_PIX_FMT_NONE
            };
        }

        private static unsafe void EncodeImagesToH264(H264VideoStreamEncoder vse, VideoFrameConverter encoderConverter, int fps, List<AVFrame> frames, List<Bitmap> bitmaps, bool useBitmapFiles, AVFormatContext* output_format_context)
        {
            Size sourceSize;
            string[] frameFiles = null;

            if (useBitmapFiles)
            {
                frameFiles = Directory.GetFiles(".", "frame.*.jpg").OrderBy(x => x).ToArray();
                var fistFrameImage = Image.FromFile(frameFiles.First());
                sourceSize = fistFrameImage.Size;
            }
            else
            {
                var first = frames.First();
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
                        convertedFrame.pts = f.pts / fps;
                    vse.Encode(convertedFrame, output_format_context, ticks++);
                }

                Console.WriteLine($"frame: {frameNumber}");
                frameNumber++;
            }

            vse.CloseAndTrailingOutput(output_format_context);
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
    }
}
