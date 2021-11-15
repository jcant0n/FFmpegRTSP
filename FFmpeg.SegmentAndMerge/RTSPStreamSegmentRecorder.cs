using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FFmpeg.SegmentAndMerge
{
    public unsafe class RTSPStreamSegmentRecorder
    {
        public static unsafe void Initialize()
        {
            Console.WriteLine("Current directory: " + Environment.CurrentDirectory);
            Console.WriteLine("Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32");
            FFmpegBinariesHelper.RegisterFFmpegBinaries();
            Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");
            SetupLogging();
            //ConfigureHWDecoder(out var deviceType);
        }

        public class SegmentRecordOptions
        {
            public bool usebitmapFiles;
            public int captureSegmentSCount;
            public int framesPerSegment;

            public AVPixelFormat sourcePixelFormatCodec;
            public AVHWDeviceType HWDevice;

            public SegmentRecordOptions()
            {
                HWDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                usebitmapFiles = false;
                sourcePixelFormatCodec = AVPixelFormat.AV_PIX_FMT_BGR24;
                framesPerSegment = 500;
                captureSegmentSCount = int.MaxValue;
            }
        }

        static AVFormatContext* input_format_context_file;
        public static unsafe void RecordSegmentedRtspStream(string url, SegmentRecordOptions options)
        {
            VideoStreamDecoder vsd;
            VideoFrameConverter decoderConverter;

            //------------------------------------------------------

            vsd = new VideoStreamDecoder(url, options.HWDevice);
            Console.WriteLine($"codec name: {vsd.CodecName}");
            var info = vsd.GetContextInfo();
            info.ToList().ForEach(x => Console.WriteLine($"{x.Key} = {x.Value}"));
            var sourceSize = vsd.FrameSize;
            var sourcePixelFormat = options.HWDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
                ? vsd.PixelFormat
                : GetHWPixelFormat(options.HWDevice);
            var destinationSize = sourceSize;
            var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_BGR24;

            //----------------------------------------------------------
            decoderConverter = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat);
            int videoStreamIndex = 1;
            var timebase = vsd._pFormatContext->streams[videoStreamIndex]->time_base;
            float fps = (timebase.num * 1000) / (float)timebase.den;
            //float speedfactor = 1f;
            float speedfactor = fps;
            Console.WriteLine("FPS: " + speedfactor);

            //----------------------------------------------------------
            var sourcePixelFormatCodec = AVPixelFormat.AV_PIX_FMT_BGR24;//AVPixelFormat.AV_PIX_FMT_BGR24;
            var destinationSizeCodec = sourceSize;
            var destinationPixelFormatCodec = AVPixelFormat.AV_PIX_FMT_YUV420P;
            //----------------------------------------------------------------
            // workaround we need a template mp4 file
            var inputFileName = "template.mp4";
            input_format_context_file = Utils.CreateInputContextFromFile(inputFileName);

            var encoderConverter = new VideoFrameConverter(sourceSize, sourcePixelFormatCodec, destinationSizeCodec, destinationPixelFormatCodec);
            var vse = new H264VideoStreamEncoder(null, timebase, destinationSize);

            //-----------------------------------------------------------------
            for (int i = 0; i < options.captureSegmentSCount; i++)
            {
                var framesBuffer = new List<AVFrame>();
                var bitmaps = new List<Bitmap>();

                //output_format_context = Utils.CreateOuputContextFile(vsd._pCodecContext->codec_id, sourceSize, vsd._pCodecContext->bit_rate, timebase, $"segment_{i:0000}b.mp4");
                Console.WriteLine("Decoding...");
                Utils.DecodeAllFramesToImages(vsd, decoderConverter, options.HWDevice, framesBuffer, bitmaps, options.framesPerSegment, generateFiles: options.usebitmapFiles);
                Console.WriteLine("Encoding...");
                //Utils.EncodeImagesToH264(vse, encoderConverter, speedfactor, framesBuffer, bitmaps, usebitmapFiles, output_format_context);

                EncodeOuputSegment($"segment_{i:0000}.mp4", vsd, vse, encoderConverter, options.usebitmapFiles, sourceSize, destinationSize, timebase, speedfactor, sourcePixelFormatCodec, destinationSizeCodec, destinationPixelFormatCodec, framesBuffer.ToList(), bitmaps.ToList());
            }
        }

        private static unsafe void EncodeOuputSegment(string fname, VideoStreamDecoder vsd, H264VideoStreamEncoder vse, VideoFrameConverter encoderConverter, bool usebitmapFiles, Size sourceSize, Size destinationSize, AVRational timebase, float speedfactor, AVPixelFormat sourcePixelFormatCodec, Size destinationSizeCodec, AVPixelFormat destinationPixelFormatCodec, List<AVFrame> framesBuffer, List<Bitmap> bitmaps)
        {
            Task.Run(() =>
            {
                lock (vse)
                {
                    AVFormatContext* output_format_context = Utils.CreateOuputContextFile(input_format_context_file, vsd._pCodecContext->codec_id, sourceSize, vsd._pCodecContext->bit_rate, timebase, 0, 0, default(AVRational), fname);

                    Utils.EncodeImagesToH264(vse, encoderConverter, speedfactor, framesBuffer, bitmaps, usebitmapFiles, output_format_context);
                }
                //bitmaps.Clear();
                //framesBuffer.Clear();
            });
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

    }
}
