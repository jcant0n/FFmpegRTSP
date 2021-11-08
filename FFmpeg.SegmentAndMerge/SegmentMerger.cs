using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FFmpeg.AutoGen.Example
{
    internal class SegmentMerger
    {
        public static unsafe void MergeSegments(string inputFilePatern, string outputFilePath, TimeSpan startTime, TimeSpan recordDuration, AVHWDeviceType HWDevice)
        {
            var frameFiles = Directory.GetFiles(".", inputFilePatern).OrderBy(x => x).ToArray();
            var inputFiles = new List<IntPtr>();

            foreach (var fname in frameFiles)
            {
                var input_format_context_file = Utils.CreateInputContextFromFile(fname);
                if (input_format_context_file == null)
                {
                    continue;
                }

                inputFiles.Add((IntPtr)input_format_context_file);
            }

            var first = (AVFormatContext*)inputFiles.First();

            Size sourceSize = new Size(first->streams[0]->codec->width, first->streams[0]->codec->height);

            var codec = first->streams[0]->codec;
            AVFormatContext* output_format_context_file = Utils.CreateOuputContextFile(first, codec->codec_id, sourceSize, codec->bit_rate, codec->time_base, outputFilePath);
            TimeSpan endTime = startTime + recordDuration;

            long tick = 0;

            TimeSpan accumulatedDuration = default(TimeSpan);
            foreach (var input in inputFiles)
            {
                var input_format_context = (AVFormatContext*)input;

                var timebase = input_format_context->streams[0]->time_base;
                var segmentDuration = TimeSpan.FromSeconds(input_format_context->streams[0]->duration * timebase.num / (float)timebase.den);
                var framesCount = input_format_context->streams[0]->nb_frames;
                var fps = framesCount / (float)segmentDuration.TotalSeconds;

                if (accumulatedDuration + segmentDuration >= startTime && accumulatedDuration <= endTime)
                {
                    var internalStart = startTime - accumulatedDuration;
                    int skipFrames = (int)(internalStart.TotalSeconds * fps);

                    var maxRecording = endTime - accumulatedDuration;
                    var maxFrames = maxRecording.TotalSeconds * fps;

                    var remainingFrames = framesCount - skipFrames;

                    AVPacket packet;
                    int ret;
                    long prevpts = 0;
                    int i = 0;
                    do
                    {
                        if (i > maxFrames)
                            break;

                        i++;

                        ret = ffmpeg.av_read_frame(input_format_context, &packet);
                        if (ret != 0)
                            break;

                        if (packet.pts < 0)
                            packet.pts = 0;

                        var duration = packet.pts - prevpts;

                        prevpts = packet.pts;
                        packet.pts = tick;
                        packet.dts = tick;

                        if (skipFrames > 0)
                        {
                            skipFrames--;
                            continue;
                        }

                        tick += duration;

                        //var in_file_stream = input_format_context->streams[packet.stream_index];
                        ret = ffmpeg.av_interleaved_write_frame(output_format_context_file, &packet);
                        ffmpeg.av_packet_unref(&packet);
                    }
                    while (ret == 0 || ret == -22);

                    Debug.WriteLine("video consumed");

                }

                accumulatedDuration += segmentDuration;

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
