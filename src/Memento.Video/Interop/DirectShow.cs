using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Memento.Video.Interop;

/// <summary>
/// Hand-written DirectShow COM interop (no DirectShowLib, no CsWin32), only the members the camera path uses: the
/// system device enumerator for <c>CLSID_VideoInputDeviceCategory</c>, the filter graph, the capture graph builder,
/// the capture pin's <c>IAMStreamConfig</c>, and qedit's sample grabber with its callback. Vtable order must match the
/// Windows SDK headers (<c>strmif.h</c>, <c>control.h</c>, <c>qedit.h</c>) exactly. ENGINE-NOTES.md §R.
/// </summary>
internal static unsafe class DirectShow
{
    public const int SOk = 0;
    public const int SFalse = 1;

    /// <summary><c>EC_COMPLETE</c>: the stream ended (a source that stopped sending).</summary>
    public const int EcComplete = 0x01;

    /// <summary><c>EC_ERRORABORT</c>: the graph stopped on an error (param 1 is the HRESULT).</summary>
    public const int EcErrorAbort = 0x03;

    /// <summary><c>EC_DEVICE_LOST</c>: a Plug and Play device was removed (param 2 is 0) or came back (1).</summary>
    public const int EcDeviceLost = 0x1F;

    /// <summary><c>EC_STREAM_ERROR_STOPPED</c>.</summary>
    public const int EcStreamErrorStopped = 0x06;

    /// <summary><c>VFW_E_CANNOT_CONNECT</c>: no combination of filters connects the pins (an unreadable format).</summary>
    public const int CannotConnect = unchecked((int)0x80040217);

    /// <summary><c>VFW_E_NO_ACCEPTABLE_TYPES</c>.</summary>
    public const int NoAcceptableTypes = unchecked((int)0x80040207);

    /// <summary><c>VFW_E_TYPE_NOT_ACCEPTED</c>.</summary>
    public const int TypeNotAccepted = unchecked((int)0x8004022A);

    /// <summary><c>VFW_E_NOT_FOUND</c>.</summary>
    public const int NotFound = unchecked((int)0x80040216);

    public const int ClsctxInprocServer = 0x1;

    public static readonly Guid ClsidSystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    public static readonly Guid ClsidVideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    public static readonly Guid ClsidFilterGraph = new("E436EBB3-524F-11CE-9F53-0020AF0BA770");
    public static readonly Guid ClsidCaptureGraphBuilder2 = new("BF87B6E1-8C27-11d0-B3F0-00AA003761C5");
    public static readonly Guid ClsidSampleGrabber = new("C1F400A0-3F08-11d3-9F0B-006008039E37");
    public static readonly Guid ClsidNullRenderer = new("C1F400A4-3F08-11d3-9F0B-006008039E37");

    public static readonly Guid PinCategoryCapture = new("fb6c4281-0353-11d1-905f-0000c0cc16ba");
    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid FormatVideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");
    public static readonly Guid FormatVideoInfo2 = new("F72A76A0-EB0A-11d0-ACE4-0000C0CC16BA");

    public static readonly Guid IidBaseFilter = new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    public static readonly Guid IidPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");
    public static readonly Guid IidStreamConfig = new("C6E13340-30AC-11d0-A18C-00A0C9118956");

    /// <summary>Creates an in-process COM object through a private RCW (DirectShow objects are free-threaded or both).</summary>
    public static T Create<T>(Guid clsid, Guid iid)
        where T : class
    {
        var hr = CoCreateInstance(in clsid, IntPtr.Zero, ClsctxInprocServer, in iid, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return (T)Marshal.GetUniqueObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>Releases an RCW now rather than at the next garbage collection (a filter holds the device).</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    /// <summary>Frees a media type's format block and its <c>pUnk</c> (<c>FreeMediaType</c>).</summary>
    public static void FreeMediaType(ref AmMediaType type)
    {
        if (type.FormatPointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(type.FormatPointer);
            type.FormatPointer = IntPtr.Zero;
            type.FormatSize = 0;
        }

        if (type.Unknown != IntPtr.Zero)
        {
            Marshal.Release(type.Unknown);
            type.Unknown = IntPtr.Zero;
        }
    }

    /// <summary>Frees a media type allocated by a filter with <c>CoTaskMemAlloc</c> (<c>DeleteMediaType</c>).</summary>
    public static void DeleteMediaType(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        var type = Marshal.PtrToStructure<AmMediaType>(pointer);
        FreeMediaType(ref type);
        Marshal.FreeCoTaskMem(pointer);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
#pragma warning disable SYSLIB1054, CA5392 // ole32 is a known system DLL; the COM out-pointer stays raw.
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int clsContext, in Guid iid, out IntPtr instance);
#pragma warning restore SYSLIB1054, CA5392

    /// <summary><c>AM_MEDIA_TYPE</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AmMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;
        public int TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr FormatPointer;
    }

    /// <summary><c>BITMAPINFOHEADER</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    /// <summary>The fixed part of <c>VIDEOINFOHEADER</c> before its bitmap header (48 bytes).</summary>
    public const int VideoInfoHeaderBitmapOffset = 48;

    /// <summary>The fixed part of <c>VIDEOINFOHEADER2</c> before its bitmap header (72 bytes).</summary>
    public const int VideoInfoHeader2BitmapOffset = 72;

    /// <summary>Both headers keep <c>AvgTimePerFrame</c> (100 ns units) at byte 40.</summary>
    public const int AvgTimePerFrameOffset = 40;

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICreateDevEnum
    {
        /// <summary>S_FALSE and a null enumerator when the category is empty.</summary>
        [PreserveSig]
        int CreateClassEnumerator(in Guid deviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propertyName, [MarshalAs(UnmanagedType.Struct)] ref object? value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propertyName, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }

    [ComImport]
    [Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBaseFilter
    {
        // IPersist
        [PreserveSig]
        int GetClassID(out Guid classId);

        // IMediaFilter
        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Run(long start);

        [PreserveSig]
        int GetState(int millisecondsTimeout, out int state);

        [PreserveSig]
        int SetSyncSource(IntPtr clock);

        [PreserveSig]
        int GetSyncSource(out IntPtr clock);

        // IBaseFilter
        [PreserveSig]
        int EnumPins(out IntPtr enumPins);

        [PreserveSig]
        int FindPin([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr pin);

        [PreserveSig]
        int QueryFilterInfo(IntPtr info);

        [PreserveSig]
        int JoinFilterGraph(IntPtr graph, [MarshalAs(UnmanagedType.LPWStr)] string? name);

        [PreserveSig]
        int QueryVendorInfo(out IntPtr vendorInfo);
    }

    /// <summary>The filter graph manager's <c>IMediaFilter</c> (to run it without a reference clock).</summary>
    [ComImport]
    [Guid("56A86899-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaFilter
    {
        [PreserveSig]
        int GetClassID(out Guid classId);

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Run(long start);

        [PreserveSig]
        int GetState(int millisecondsTimeout, out int state);

        [PreserveSig]
        int SetSyncSource(IntPtr clock);

        [PreserveSig]
        int GetSyncSource(out IntPtr clock);
    }

    [ComImport]
    [Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphBuilder
    {
        // IFilterGraph
        [PreserveSig]
        int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);

        [PreserveSig]
        int RemoveFilter(IBaseFilter filter);

        [PreserveSig]
        int EnumFilters(out IntPtr enumFilters);

        [PreserveSig]
        int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr filter);

        [PreserveSig]
        int ConnectDirect(IntPtr pinOut, IntPtr pinIn, IntPtr mediaType);

        [PreserveSig]
        int Reconnect(IntPtr pin);

        [PreserveSig]
        int Disconnect(IntPtr pin);

        [PreserveSig]
        int SetDefaultSyncSource();

        // IGraphBuilder
        [PreserveSig]
        int Connect(IntPtr pinOut, IntPtr pinIn);

        [PreserveSig]
        int Render(IntPtr pinOut);

        [PreserveSig]
        int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.LPWStr)] string? playList);

        [PreserveSig]
        int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.LPWStr)] string? filterName, out IntPtr filter);

        [PreserveSig]
        int SetLogFile(IntPtr file);

        [PreserveSig]
        int Abort();

        [PreserveSig]
        int ShouldOperationContinue();
    }

    [ComImport]
    [Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICaptureGraphBuilder2
    {
        [PreserveSig]
        int SetFiltergraph(IGraphBuilder graph);

        [PreserveSig]
        int GetFiltergraph(out IntPtr graph);

        [PreserveSig]
        int SetOutputFileName(in Guid type, [MarshalAs(UnmanagedType.LPWStr)] string file, out IntPtr multiplexer, out IntPtr sink);

        [PreserveSig]
        int FindInterface(Guid* category, Guid* type, IBaseFilter filter, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? result);

        [PreserveSig]
        int RenderStream(Guid* category, Guid* type, [MarshalAs(UnmanagedType.IUnknown)] object source, IBaseFilter? compressor, IBaseFilter? renderer);

        [PreserveSig]
        int ControlStream(in Guid category, in Guid type, IBaseFilter filter, IntPtr start, IntPtr stop, short startCookie, short stopCookie);

        [PreserveSig]
        int AllocCapFile([MarshalAs(UnmanagedType.LPWStr)] string file, long size);

        [PreserveSig]
        int CopyCaptureFile([MarshalAs(UnmanagedType.LPWStr)] string old, [MarshalAs(UnmanagedType.LPWStr)] string next, int allowEscAbort, IntPtr callback);

        [PreserveSig]
        int FindPin([MarshalAs(UnmanagedType.IUnknown)] object source, int direction, in Guid category, in Guid type, [MarshalAs(UnmanagedType.Bool)] bool unconnected, int index, out IntPtr pin);
    }

    [ComImport]
    [Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumPins
    {
        [PreserveSig]
        int Next(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] pins, IntPtr fetched);

        [PreserveSig]
        int Skip(int count);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IntPtr clone);
    }

    /// <summary><c>IPin</c>, up to <c>QueryDirection</c> (the only member used: 0 = input, 1 = output).</summary>
    [ComImport]
    [Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPin
    {
        [PreserveSig]
        int Connect(IntPtr receivePin, IntPtr mediaType);

        [PreserveSig]
        int ReceiveConnection(IntPtr connector, IntPtr mediaType);

        [PreserveSig]
        int Disconnect();

        [PreserveSig]
        int ConnectedTo(out IntPtr pin);

        [PreserveSig]
        int ConnectionMediaType(IntPtr mediaType);

        [PreserveSig]
        int QueryPinInfo(IntPtr info);

        [PreserveSig]
        int QueryDirection(out int direction);
    }

    [ComImport]
    [Guid("C6E13340-30AC-11d0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMStreamConfig
    {
        [PreserveSig]
        int SetFormat(IntPtr mediaType);

        [PreserveSig]
        int GetFormat(out IntPtr mediaType);

        [PreserveSig]
        int GetNumberOfCapabilities(out int count, out int size);

        [PreserveSig]
        int GetStreamCaps(int index, out IntPtr mediaType, IntPtr streamConfigCaps);
    }

    [ComImport]
    [Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IMediaControl
    {
        [PreserveSig]
        int Run();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int GetState(int millisecondsTimeout, out int state);
    }

    [ComImport]
    [Guid("56A868B6-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IMediaEvent
    {
        [PreserveSig]
        int GetEventHandle(out IntPtr handle);

        [PreserveSig]
        int GetEvent(out int code, out IntPtr param1, out IntPtr param2, int millisecondsTimeout);

        [PreserveSig]
        int WaitForCompletion(int millisecondsTimeout, out int code);

        [PreserveSig]
        int CancelDefaultHandling(int code);

        [PreserveSig]
        int RestoreDefaultHandling(int code);

        [PreserveSig]
        int FreeEventParams(int code, IntPtr param1, IntPtr param2);
    }

    [ComImport]
    [Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabber
    {
        [PreserveSig]
        int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool oneShot);

        [PreserveSig]
        int SetMediaType(in AmMediaType type);

        [PreserveSig]
        int GetConnectedMediaType(out AmMediaType type);

        [PreserveSig]
        int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool bufferThem);

        [PreserveSig]
        int GetCurrentBuffer(ref int bufferSize, IntPtr buffer);

        [PreserveSig]
        int GetCurrentSample(out IntPtr sample);

        [PreserveSig]
        int SetCallback(ISampleGrabberCB? callback, int whichMethodToCallback);
    }

    [ComImport]
    [Guid("0579154A-2B53-4994-B0D0-E773148EFF85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabberCB
    {
        [PreserveSig]
        int SampleCB(double sampleTime, IntPtr sample);

        [PreserveSig]
        int BufferCB(double sampleTime, IntPtr buffer, int bufferLength);
    }
}
