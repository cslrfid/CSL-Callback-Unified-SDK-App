using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Diagnostics;

using CSLibrary.Barcode.Constants;
using CSLibrary.Barcode.Structures;
using CSLibrary.Threading;

namespace CSLibrary.Barcode
{
        

        
    /// <summary>
    /// Barcode library
    /// </summary>
    public class Barcode
    {
        #region My Custom function

        private static IntPtr triggerStartEvent = IntPtr.Zero;
        private static IntPtr triggerStopEvent = IntPtr.Zero;
        private static Thread mCaptureThread = null;
        private static ManualResetEvent mStarted = new ManualResetEvent(false);
        private static ManualResetEvent mStopped = new ManualResetEvent(false);
        private static ManualResetEvent mWaitCallback = new ManualResetEvent(false);

        /// <summary>
        /// BarcodeEventHandler Delegate 
        /// </summary>
        /// <param name="e"></param>
        public delegate void BarcodeEventHandler(BarcodeEventArgs e);
        /// <summary>
        /// BarcodeStateEventHandler Delegate 
        /// </summary>
        /// <param name="e"></param>
        public delegate void BarcodeStateEventHandler(BarcodeStateEventArgs e);

        private static event BarcodeEventHandler m_captureCompleted;
        private static event BarcodeStateEventHandler m_stateChanged;
        /// <summary>
        /// BarcodeEventHandler : Capture completed event trigger
        /// </summary>
        public static event BarcodeEventHandler OnCapturedNotify
        {
            add { lock (synlock) m_captureCompleted += value; }
            remove { lock (synlock) m_captureCompleted -= value; }
        }
        /// <summary>
        /// BarcodeStateEventHandler : report current operation
        /// </summary>
        public static event BarcodeStateEventHandler OnStateChanged
        {
            add { lock (synlock) m_stateChanged += value; }
            remove { lock (synlock) m_stateChanged -= value; }
        }
        /// <summary>
        /// True will only return decoded data in <see cref="OnCapturedNotify"/>
        /// </summary>
        public static bool bCaptureDecoded
        {
            get { lock (synlock) return b_CaptureDecoded; }
            set { lock (synlock) b_CaptureDecoded = value; }
        }
        /// <summary>
        /// Current operation state
        /// </summary>
        public static BarcodeState State
        {
            get { lock (synlock) return m_state; }
            protected set { lock (synlock) m_state = value; }
        }
        /*internal static bool bStop
        {
            get { lock (synlock) return b_Stop; }
            set { lock (synlock) b_Stop = value; }
        }*/

        private static BarcodeState m_state = BarcodeState.IDLE;
        // Helper for marshalling execution to GUI thread
#if !__NO_GUI_MARSHALLER__
        private static System.Windows.Forms.Control mGuiMarshaller;
#endif
        private static bool b_CaptureDecoded = true;
        private static int mStop = 0;
        private static object synlock = new object();

        private static void FireStateChangedEvent(BarcodeState e)
        {
            State = e;
#if !__NO_GUI_MARSHALLER__
            if (mGuiMarshaller.InvokeRequired)
            {
                mGuiMarshaller.Invoke(new BarcodeStateEventHandler(TellThemStateChanged), new object[] { new BarcodeStateEventArgs(e) });
            }
            else
#endif
            {
                TellThemStateChanged(new BarcodeStateEventArgs(e));
            }
        }

        private static void FireCaptureCompletedEvent(BarcodeEventArgs e)
        {
#if !__NO_GUI_MARSHALLER__
            if (mGuiMarshaller.InvokeRequired)
            {
                mGuiMarshaller.Invoke(new BarcodeEventHandler(TellThemCaptureCompleted), new object[] { e });
            }
            else
#endif
            {
                TellThemCaptureCompleted(e);
            }
        }

        private static void TellThemCaptureCompleted(BarcodeEventArgs e)
        {
            if (m_captureCompleted != null)
            {
                m_captureCompleted(e);
            }
        }

        private static void TellThemStateChanged(BarcodeStateEventArgs e)
        {
            if (m_stateChanged != null)
            {
                m_stateChanged(e);
            }
        }
        /// <summary>
        /// Enhancement for BizTalk
        /// </summary>
        /// <param name="eventType"></param>
        /// <param name="dwBytes"></param>
        /// <returns></returns>
        private static int internalCb
        (
            [In] uint eventType,
            [In] uint dwBytes
        )
        {
            Debug.WriteLine("WM_SDK_EVENT_HWND_MSG");
            string tcErrMsg = "";                       // Error message buffer.
            Result nResult = Result.INITIALIZE;         // Return code.
            EventType evt = (EventType)eventType;
            try
            {
                mWaitCallback.Reset();
                if (evt == EventType.BARCODE_EVENT)          // Verify the event type is barcode
                {
                    #region Barcode Message
                    Debug.WriteLine("BARCODE_EVENT");
                    if (bCaptureDecoded)
                    {
                        DecodeMessage decodeInfo = new DecodeMessage();       // Decode message structure.
                        if ((nResult = GetAsyncResult(ref evt, decodeInfo)) != Result.SUCCESS)
                        {
                            GetErrorMessage(nResult, ref tcErrMsg);
                            throw new System.Exception(tcErrMsg);
                        }
                        if (m_captureCompleted != null)
                            m_captureCompleted(new BarcodeEventArgs(MessageType.DEC_MSG, decodeInfo));
                    }
                    else
                    {
                        RawDecodeMessage rawInfo = new RawDecodeMessage();
                        if ((nResult = GetAsyncResult(ref evt, rawInfo)) != Result.SUCCESS)
                        {
                            GetErrorMessage(nResult, ref tcErrMsg);
                            throw new System.Exception(tcErrMsg);
                        }
                        if (m_captureCompleted != null)
                            m_captureCompleted(new BarcodeEventArgs(MessageType.RAW_MSG, rawInfo));

                    }
                    MonitorEx.SetEvent(triggerStartEvent, MonitorEx.EventFlags.SET);
                    #endregion
                }
            }
            catch
            {
                //if (m_stateChanged != null)
                //    m_stateChanged(new BarcodeStateEventArgs(BarcodeState.IDLE));
                //bStop = false;
                mWaitCallback.Set();
                return 0;
            }
            mWaitCallback.Set();
            return 1;
        }


        /// <summary>
        /// Start to capture barcode, until stop is sent.
        /// </summary>
        /// <returns></returns>
        public static bool Start()
        {
            try
            {
                if (!EngineConnected() || State != BarcodeState.IDLE)
                    return false;
#if !WindowsCE
                //Initial Event
                MonitorEx.SetEvent(triggerStartEvent, MonitorEx.EventFlags.RESET);
                MonitorEx.SetEvent(triggerStopEvent, MonitorEx.EventFlags.RESET);

                Result nResult = Result.INITIALIZE;  // Return code.
                // Register the message window with the SDK. 
                if ((nResult = Barcode.SetAsyncMethods(IntPtr.Zero, IntPtr.Zero, new EventCallback(internalCb))) == Result.SUCCESS)
                {
                    // Call the SDK function to capture a barcode,
                    // no timeout.  Unless call fails, you will get a message when command completes.
                    if (!IsAlive())
                    {
                        mCaptureThread = new Thread(new ThreadStart(threadStart));
                        mCaptureThread.IsBackground = true;
                        mCaptureThread.Start();
                        mStarted.WaitOne();
                        // Trigger start once
                        MonitorEx.SetEvent(triggerStartEvent, MonitorEx.EventFlags.SET);
                    }

                }
                else
                {
                    throw new System.Exception(nResult.ToString());
                }
#else
                if (IsAlive())
                {
                    Thread.Sleep(1000);
                }
                
                if (!IsAlive())
                {
                    mStarted.Reset();
                    Interlocked.Exchange(ref mStop, 0);
                    mCaptureThread = new Thread(new ThreadStart(ReadThread));
                    mCaptureThread.IsBackground = true;
                    mCaptureThread.Start();
                    mStarted.WaitOne();
                }
                else
                {
                    return false;
                }
#endif

            }
            catch (System.Exception ex)
            {
#if BUILD_WITH_SYSLOG
                CSLibrary.SysLogger.LogError(ex);
#endif
                FireStateChangedEvent(BarcodeState.IDLE);
                return false;
            }

            FireStateChangedEvent(BarcodeState.BUSY);
            //bStop = false;
            return true;
        }

        private static void ReadThread()
        {
            Result result = Result.SUCCESS;
            mStarted.Set();
            try
            {
                while (!Interlocked.Equals(mStop, 1))
                {
                    if (bCaptureDecoded)
                    {
                        DecodeMessage decodeInfo = new DecodeMessage();       // Decode message structure.
                        result = Barcode.CaptureBarcode(decodeInfo, 500, true);
                        if (result == Result.ERR_NODECODE)
                        {
                            continue;
                        }
                        else if (result != Result.SUCCESS)
                        {
                            ErrorMessage msg = new ErrorMessage();
                            GetErrorMessage(msg);
                            if (m_captureCompleted != null)
                                m_captureCompleted(new BarcodeEventArgs(MessageType.ERR_MSG, msg));

                        }
                        else
                        {
                            if (m_captureCompleted != null)
                                m_captureCompleted(new BarcodeEventArgs(MessageType.DEC_MSG, decodeInfo));
                        }
                    }
                    else
                    {
                        RawDecodeMessage rawInfo = new RawDecodeMessage();
                        result = Barcode.CaptureRawBarcode(rawInfo, 500, true);
                        if (result == Result.ERR_NODECODE)
                        {
                            continue;
                        }
                        else if (result != Result.SUCCESS)
                        {
                            ErrorMessage msg = new ErrorMessage();
                            GetErrorMessage(msg);
                            if (m_captureCompleted != null)
                                m_captureCompleted(new BarcodeEventArgs(MessageType.ERR_MSG, msg));
                        }
                        else
                        {
                            if (m_captureCompleted != null)
                                m_captureCompleted(new BarcodeEventArgs(MessageType.RAW_MSG, rawInfo));
                        }
                    }
                }
            }
            catch { }
            finally { }
            mStopped.Set();
            FireStateChangedEvent(BarcodeState.IDLE);

        }

        private static void threadStart()
        {
            mStarted.Set();
            while (true)
            {
                //Wait here until stop or next start trigger

                switch (MonitorEx.WaitForMultipleObjects(2, new IntPtr[] { triggerStartEvent, triggerStopEvent }, false, MonitorEx.INFINITE))
                {
                    case MonitorEx.WAIT_TIMEOUT:
                        break;
                    case MonitorEx.WAIT_FAILED:
                        break;
                    case 0:
                        //startevent trigger
                        MonitorEx.SetEvent(triggerStartEvent, MonitorEx.EventFlags.RESET);
                        if (bCaptureDecoded)
                        {
                            if (Barcode.CaptureBarcode(null, 0, false) != Result.SUCCESS)
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (Barcode.CaptureRawBarcode(null, 0, false) != Result.SUCCESS)
                            {
                                break;
                            }
                        }
                        break;
                    case 1:
                        //stop event trigger
                        MonitorEx.SetEvent(triggerStopEvent, MonitorEx.EventFlags.RESET);
                        Barcode.CancelIo();
                        goto STOP;
                }

                /*while (true)
                {
                    if (nextStartEvent.WaitOne(10, false))
                    {
                        goto NEXTSTART;
                    }
                    if (stopEvent.WaitOne(10, false))
                    {
                        goto STOP;
                    }
                }*/

            }
            STOP:
            FireStateChangedEvent(BarcodeState.IDLE);
            mStarted.Reset();
            mWaitCallback.WaitOne();
            mStopped.Set();
        }

        /// <summary>
        /// Stop capturing
        /// </summary>
        /// <returns></returns>
        public static bool Stop()
        {
            bool rc = true;
            try
            {
                if (IsAlive())
                {
                    FireStateChangedEvent(BarcodeState.STOPPING);

#if WindowsCE
                    Interlocked.Exchange(ref mStop, 1);
#else
                    MonitorEx.SetEvent(triggerStopEvent, MonitorEx.EventFlags.SET);
#endif
                    mStopped.WaitOne();

                }
                else if(State == BarcodeState.IDLE)
                {
                    //bStop = true;
                }
            }
            catch (System.Exception ex)
            {
#if BUILD_WITH_SYSLOG
                CSLibrary.SysLogger.LogError(ex);
#endif          
                rc = false;
            }
            return rc;
        }
        #endregion

        /// <summary>
        /// This function opens a connection to an imager.  
        /// The connection must be closed by  calling Disconnect(). 
        /// The caller can verify that the imager is connected by calling EngineConnected().
        /// </summary>
        /// <returns></returns>
        public static Result Connect/*OK*/
        (
        )
        {
             Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            if (EngineConnected())
                return Result.SUCCESS;

            try
            {
#if !__NO_GUI_MARSHALLER__
                if (mGuiMarshaller == null)
                {
                    mGuiMarshaller = new System.Windows.Forms.Control();
                }
#endif
                triggerStartEvent = MonitorEx.CreateEvent(IntPtr.Zero, false, false, null);
                triggerStopEvent = MonitorEx.CreateEvent(IntPtr.Zero, false, false, null);

                SerialPortParms cfg = new SerialPortParms();
                cfg.baudRate = BaudRate.SERIAL_BAUD_115200;
                cfg.bAutoBaud = Bool.FALSE;
                cfg.dataBits = DataBit.BITS_8;
                cfg.parity = Parity.NONE;
                cfg.stopBits = StopBit.ONE;

                pParms = Marshal.AllocHGlobal(Marshal.SizeOf(cfg));

                Marshal.StructureToPtr(cfg, pParms, false);

                result = Native.hhpConnect(ComPort.COM1, pParms);
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }

        static void msgWnd_m_stateChanged(BarcodeStateEventArgs e)
        {
            FireStateChangedEvent(e.State);
        }

        static void msgWnd_m_captureCompleted(BarcodeEventArgs e)
        {
            FireCaptureCompletedEvent(e);
        }
        /// <summary>
        /// Closes the communications port and stops the read data thread. 
        /// </summary>
        /// <returns></returns>
        public static Result Disconnect()/*OK*/
        {
            if (!EngineConnected())
                return Result.INITIALIZE;

            //Check and wait until idle mode
            if (State != BarcodeState.IDLE)
            {
                Stop();
            }

#if !__NO_GUI_MARSHALLER__
            if (mGuiMarshaller != null)
            {
                mGuiMarshaller.Dispose();
                mGuiMarshaller = null;
            }
#endif
            if (triggerStartEvent != IntPtr.Zero)
            {
                MonitorEx.CloseHandle(triggerStartEvent);
                triggerStartEvent = IntPtr.Zero;
            }
            if (triggerStopEvent != IntPtr.Zero)
            {
                MonitorEx.CloseHandle(triggerStopEvent);
                triggerStopEvent = IntPtr.Zero;
            }
            return Native.hhpDisconnect();
        }
        /// <summary>
        /// This function determines whether the imager is connected.  
        /// This function checks to to see if the imager has lost power (due to 
        /// the host going into a suspended state), or if the imager has been removed.  
        /// </summary>
        /// <returns></returns>
        public static bool EngineConnected()/*OK*/
        {
            return Native.hhpEngineConnected();
        }
        /// <summary>
        /// This function returns a text message describing the meaning of a Result_t error code.
        /// See Error Codes on page 3-1 for complete descriptions.
        /// </summary>
        /// <param name="result">Error code returned from one of the other 5X80 SDK functions.</param>
        /// <param name="msg">hold error message string.</param>
        /// <returns></returns>
        public static Result GetErrorMessage/*OK*/
        (
            [In, Out] Result result,
            [In, Out] ref string msg
        )
        {
            StringBuilder errorMsg = new StringBuilder(128);
            Result rc = Native.hhpGetErrorMessage(result, errorMsg, 128);
            if (rc == Result.SUCCESS)
            {
                msg = errorMsg.ToString();
            }
            return rc;
        }/// <summary>
        /// This function returns a text message describing the meaning of a Result_t error code.
        /// See Error Codes on page 3-1 for complete descriptions.
        /// </summary>
        /// <param name="result">Error code returned from one of the other 5X80 SDK functions.</param>
        /// <param name="msg">hold error message string.</param>
        /// <returns></returns>
        public static Result GetErrorMessage/*OK*/
        (
            [In, Out] ErrorMessage msg
        )
        {
            if (msg == null)
                return Result.ERR_INVALID_PARAMETER;
            StringBuilder errorMsg = new StringBuilder(128);
            Result rc = Native.hhpGetErrorMessage(msg.result, errorMsg, 128);
            if (rc == Result.SUCCESS)
            {
                msg.message = errorMsg.ToString();

            }
            return rc;
        }

        /// <summary>
        /// SetAsyncMethods sets the methods by which the caller wishes to be notified upon receipt of a barcode or image.  
        /// </summary>
        /// <param name="hEventHandle">Handle to a Windows Event.  The event should specify manual reset.</param>
        /// <param name="hWndHandle">Handle to the application window that should receive the SDK defined message 
        /// WM_SDK_EVENT_HWND_MSG.  The message parameters are:  
        /// WPARAM The event type (EventType) 
        /// LPARAM The number of bytes received</param>
        /// <param name="callback">Callback function of type EVENTCALLBACK, which is BOOL CALLBACK name ( 
        /// EventType,DWORD ).  </param>
        /// <returns></returns>
        public static Result SetAsyncMethods
        (
            [In] IntPtr hEventHandle,
            [In] IntPtr hWndHandle,
            [In] EventCallback callback
        )
        {
            Result result = Result.SUCCESS;
            //IntPtr lpBufPtr = IntPtr.Zero;
            try
            {
                /*if (callback == null)
                {
                    return Result.ERR_INVALID_PARAMETER;
                }*/

                //lpBufPtr = Marshal.AllocHGlobal(IntPtr.Size);
                               
               // Marshal.WriteIntPtr(lpBufPtr, Marshal.GetFunctionPointerForDelegate(callback));

                result = Native.hhpSetAsyncMethods(hEventHandle, hWndHandle, callback);
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            /*if (IntPtr.Zero != lpBufPtr)
            {
                Marshal.FreeHGlobal(lpBufPtr);
            }*/

            return result;

        }
        
        /// <summary>
        /// Retrieves the data from the last signal event (image/barcode capture).  
        /// This function can be called with pResultStruct set to NULL 
        /// to obtain the event type.  This is useful when the notification method is a Windows event.
        /// </summary>
        /// <param name="pEventType">Type of data causing the event notification. </param>
        /// <param name="pResultStruct"> An DECODE_MSG , TEXT_MSG or IMAGE structure, depending on the 
        /// value of hEventType.  This parameter can be NULL if just the event type is desired.  This is of use 
        /// when the Event Handle notification is used.</param>
        /// <returns></returns>
        public static Result GetAsyncResult/*OK MSG Catpure*/
        (
            [In, Out]   ref EventType pEventType,
            [In, Out]   MessageBase pResultStruct
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            if (pResultStruct == null)
                return Result.ERR_INVALID_PARAMETER;

            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                pParms = Marshal.AllocHGlobal((int)pResultStruct.length);

                Marshal.StructureToPtr(pResultStruct, pParms, false);

                result = Native.hhpGetAsyncResult(ref pEventType, pParms);

                if (result == Result.SUCCESS)
                {
                    Marshal.PtrToStructure(pParms, pResultStruct);
                }

            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;

        }
        /*
        /// <summary>
        /// Retrieves the data from the last signal event (image/barcode capture).  
        /// This function can be called with pResultStruct set to NULL 
        /// to obtain the event type.  This is useful when the notification method is a Windows event.
        /// </summary>
        /// <param name="pEventType">Type of data causing the event notification. </param>
        /// <param name="pResultStruct"> TEXT_MSG structure, depending on the 
        /// value of hEventType.  This parameter can be NULL if just the event type is desired.  This is of use 
        /// when the Event Handle notification is used.</param>
        /// <returns></returns>
        public static Result GetAsyncResult
        (
            [In, Out]   ref EVENT_TYPE pEventType,
            [In, Out]   CFG_RAW_DECODE_MSG pResultStruct
        )
        {
            return Native.hhpGetAsyncResult(ref pEventType, pResultStruct);
        }

        /// <summary>
        /// Retrieves the data from the last signal event (image/barcode capture).  
        /// This function can be called with pResultStruct set to NULL 
        /// to obtain the event type.  This is useful when the notification method is a Windows event.
        /// </summary>
        /// <param name="pEventType">Type of data causing the event notification. </param>
        /// <param name="pResultStruct">  IMAGE depending on the 
        /// value of hEventType.  This parameter can be NULL if just the event type is desired.  This is of use 
        /// when the Event Handle notification is used.</param>
        /// <returns></returns>
        public static Result GetAsyncResult
        (
            [In, Out]   ref EVENT_TYPE pEventType,
            [In, Out]   CFG_IMAGE pResultStruct
        )
        {
            return Native.hhpGetAsyncResult(ref pEventType, pResultStruct);
        }*/

        /// <summary>
        /// Cancels the current barcode or image capture.
        /// </summary>
        /// <returns></returns>
        public static Result CancelIo()/*OK*/
        {
            return Native.hhpCancelIo();
        }
        /// <summary>
        /// Reads the configuration items for one or all of the configuration structures found in the main 5X80 SDK configuration structure 
        /// CFG_ITEMS.
        /// </summary>
        /// <param name="cfgType">Use SETUP_TYPE.CURRENT for the current settings, or SETUP_TYPE.DEFAULT for the  
        /// customer default settings.</param>
        /// <param name="item">enumerated type CFG_ITEMS</param>
        /// <param name="pStruct">tructure based on item:</param>
        /// <returns></returns>
        public static Result ReadConfigItem/*OK*/
        (
            [In]        SetupType cfgType,
            [In]        ConfigItems item,
            [In, Out]   ConfigBase pStruct
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            if (pStruct == null)
                return Result.ERR_INVALID_PARAMETER;

            Type gType = pStruct.GetType();
            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                switch (item)
                {
                    case ConfigItems.ALL_CONFIG:
                        if (gType != typeof(AllConfigParms))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.WriteInt32(pParms, (int)pStruct.length);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                        {
                            ReadAllConfigStructures(pParms, (AllConfigParms)pStruct);
                        }

                        break;
                    case ConfigItems.BEEPER_CONFIG:
                        if (gType != typeof(BeeperParms))
                            return Result.ERR_INVALID_PARAMETER;
                        
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.DECODER_CONFIG:
                        if (gType != typeof(DecoderParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.IMAGE_ACQUISITION:
                        if (gType != typeof(ImageAcquisitionParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.IMAGE_TRANSFER:
                        if (gType != typeof(ImageTransferParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.POWER_CONFIG:
                        if (gType != typeof(PowerParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.SEQUENCE_CONFIG:
                        if (gType != typeof(SequenceModeParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        int index = 0;
                        
                        WriteInt32(pParms, ref index, pStruct.length);

                        WriteInt32(pParms, ref index, (int)((SequenceModeParms)pStruct).dwMask);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                        {

                            ((SequenceModeParms)pStruct).dwMask = (SequenceMask)Marshal.ReadInt32(new IntPtr(pParms.ToInt64() + 4));

                            ((SequenceModeParms)pStruct).sequenceMode = (SequenceMode)Marshal.ReadInt32(new IntPtr(pParms.ToInt64() + 8));

                            ((SequenceModeParms)pStruct).dwNumBarCodes = (uint)Marshal.ReadInt32(new IntPtr(pParms.ToInt64() + 12));

                            for (int i = 0; i < Constants.Constants.MAX_SEQ_BARCODES; i++)
                            {
                                SequenceBarcodeParms temp = new SequenceBarcodeParms();
                                Marshal.PtrToStructure(new IntPtr(pParms.ToInt64() + 16 + i * 76), temp);
                                ((SequenceModeParms)pStruct).seqBarCodes[i] = temp;
                            }
                        }
                        break;
                    case ConfigItems.SERIAL_PORT_CONFIG:
                        if (gType != typeof(SerialPortParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.SYMBOLOGY_CONFIG:
                        if (gType != typeof(SymbologyParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.WriteInt32(pParms, (int)pStruct.length);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            ReadAllSymbol(pParms, (SymbologyParms)pStruct);

                        break;
                    case ConfigItems.TRIGGER_CONFIG:
                        if (gType != typeof(TriggerParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);

                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;
                    case ConfigItems.VERSION_INFO:
                        if (gType != typeof(VersionParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpReadConfigItem(cfgType, item, pParms);
                        
                        if (result == Result.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);

                        break;

                }

            }
            catch (OutOfMemoryException)
            {
                return Result.ERR_MEMORY;
            }

            if (pParms != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }
        /// <summary>
        /// Writes the configuration items for one or all of the configuration structures found in the main 5X80 SDK configuration structure 
        /// CFG_ITEMS.  Individual items can be specified by adding the appropriate mask bit by ORing it with the dwMask member of 
        /// the structure.  Only items whose bits are set are written; all other items are ignored.
        /// </summary>
        /// <param name="item">enumerated type CFG_ITEMS</param>
        /// <param name="pStruct">tructure based on item:</param>
        /// <returns></returns>
        public static Result WriteConfigItem/*OK*/
        (
            [In]        ConfigItems item,
            [In]        ConfigBase pStruct
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            if (pStruct == null)
                return Result.ERR_INVALID_PARAMETER;

            Type gType = pStruct.GetType();
            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                switch (item)
                {
                    case ConfigItems.ALL_CONFIG:
                        if (gType != typeof(AllConfigParms))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        WriteAllConfigStructures(pParms, (AllConfigParms)pStruct);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.BEEPER_CONFIG:
                        if (gType != typeof(BeeperParms))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.DECODER_CONFIG:
                        if (gType != typeof(DecoderParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.IMAGE_ACQUISITION:
                        if (gType != typeof(ImageAcquisitionParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.IMAGE_TRANSFER:
                        if (gType != typeof(ImageTransferParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.POWER_CONFIG:
                        if (gType != typeof(PowerParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.SEQUENCE_CONFIG:
                        if (gType != typeof(SequenceModeParms))
                            return Result.ERR_INVALID_PARAMETER;

                        int index = 0;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        WriteInt32(pParms, ref index, ((SequenceModeParms)pStruct).length);

                        WriteInt32(pParms, ref index, (int)((SequenceModeParms)pStruct).dwMask);
                        WriteInt32(pParms, ref index, (int)((SequenceModeParms)pStruct).sequenceMode);
                        WriteInt32(pParms, ref index, (int)((SequenceModeParms)pStruct).dwNumBarCodes);

                        for (int i = 0; i < ((SequenceModeParms)pStruct).seqBarCodes.Length; i++)
                        {
                            StructureToPtr(pParms, ref index, ((SequenceModeParms)pStruct).seqBarCodes[i]);
                        }

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.SERIAL_PORT_CONFIG:
                        if (gType != typeof(SerialPortParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.SYMBOLOGY_CONFIG:
                        if (gType != typeof(SymbologyParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        WriteAllSymbol(pParms, (SymbologyParms)pStruct);    

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.TRIGGER_CONFIG:
                        if (gType != typeof(TriggerParms))
                            return Result.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.StructureToPtr(pStruct, pParms, false);

                        result = Native.hhpWriteConfigItem(item, pParms);

                        break;
                    case ConfigItems.VERSION_INFO:
                        /*if (gType != typeof(CFG_VERSION_INFO))
                            return RESULT.ERR_INVALID_PARAMETER;
                        pParms = Marshal.AllocHGlobal((int)pStruct.length);

                        Marshal.WriteInt32(pParms, (int)pStruct.length);

                        result = Native.hhpWriteConfigItem(item, item, pParms);

                        if (result == RESULT.SUCCESS)
                            Marshal.PtrToStructure(pParms, pStruct);
                        */
                        return Result.SUCCESS;

                }

            }
            catch (OutOfMemoryException)
            {
                return Result.ERR_MEMORY;
            }

            if (pParms != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }
        /// <summary>
        /// Defaults a configuration group or individual group structure items.
        /// </summary>
        /// <param name="item">One of the members of the enumerated type CFG_ITEMS.</param>
        /// <returns></returns>
        public static Result SetConfigItemToDefaults
        (
            [In]        ConfigItems item
        )
        {
            return Native.hhpSetConfigItemToDefaults(item);
        }

        /// <summary>
        /// Returns the fixed imager capabilities, such as imager bits per pixel or image capture width and height.
        /// </summary>
        /// <param name="pImgrCaps">IMAGER_CAPS structure</param>
        /// <returns></returns>
        public static Result ReadImagerCapabilities/*OK*/
        (
            [In, Out] ImagerCapabilitiesParms pImgrCaps
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result rc = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                pParms = Marshal.AllocHGlobal(Marshal.SizeOf(pImgrCaps));

                Marshal.StructureToPtr(pImgrCaps, pParms, false);

                rc = Native.hhpReadImagerCapabilities(pParms);

                if(rc == Result.SUCCESS)
                {
                    Marshal.PtrToStructure(pParms, pImgrCaps);
                }

            }
            catch (OutOfMemoryException)
            {
                return Result.ERR_MEMORY;
            }
            if (pParms != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pParms);
            }
            return rc;
        }
        /// <summary>
        /// Reads configuration items for a single symbology or for all symbologies. 
        /// Individual items to be read are specified by adding the 
        /// appropriate mask bit (OR it) to the mask member of the structure to which it belongs.
        /// Only items whose bits are set are read; all other items are ignored.
        /// </summary>
        /// <param name="cfgType">Use SETUP_TYPE_CURRENT for the current settings, or SETUP_TYPE_DEFAULT for the  
        /// customer default settings.</param>
        /// <param name="nSymbol">One of the symbology enumerated types, e.g., SYMBOL.CODE39, SYMBOL.OCR, or SYMBOL.ALL to read all 
        /// symbologies.</param>
        /// <param name="pvSymStruct"> structure based on nSymbol</param>
        /// <returns></returns>
        public static Result ReadSymbologyConfig/*OK*/
        (
            [In]        SetupType cfgType,
            [In]        Symbol nSymbol,
            [In, Out]   ConfigBase pvSymStruct
         )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            if (pvSymStruct == null)
                return Result.ERR_INVALID_PARAMETER;

            Type gType = pvSymStruct.GetType();
            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {

                switch (nSymbol)
                {
                    case Symbol.ALL:
                        if (gType != typeof(SymbologyParms))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pvSymStruct.length);

                        Marshal.WriteInt32(pParms, 0, (int)pvSymStruct.length);

                        result = Native.hhpReadSymbologyConfig(cfgType, nSymbol, pParms);

                        if (Result.SUCCESS == result)
                        {
                            ReadAllSymbol(pParms, (SymbologyParms)pvSymStruct);
                        }
                        break;
                    case Symbol.AZTEC:
                    case Symbol.CODABAR:
                    case Symbol.CODE11:
                    case Symbol.CODE128:
                    case Symbol.CODE39:
                    case Symbol.CODE49:
                    case Symbol.CODE93:
                    case Symbol.COMPOSITE:
                    case Symbol.DATAMATRIX:
                    case Symbol.INT25:
                    case Symbol.MAXICODE:
                    case Symbol.MICROPDF:
                    case Symbol.PDF417:
                    case Symbol.QR:
                    case Symbol.RSS:
                    case Symbol.IATA25:
                    case Symbol.CODABLOCK:
                    case Symbol.MSI:
                    case Symbol.MATRIX25:
                    case Symbol.KORPOST:
                    case Symbol.STRT25:
                    case Symbol.PLESSEY:
                    case Symbol.CHINAPOST:
                    case Symbol.TELEPEN:
                    case Symbol.CODE16K:
                    case Symbol.POSICODE:
                        if (gType != typeof(SymFlagsRange))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pvSymStruct.length);

                        Marshal.StructureToPtr(pvSymStruct, pParms, false);

                        result = Native.hhpReadSymbologyConfig(cfgType, nSymbol, pParms);

                        if (Result.SUCCESS == result)
                        {
                            Marshal.PtrToStructure(pParms, pvSymStruct);
                        }
                        break;
                    case Symbol.MESA:
                    case Symbol.EAN8:
                    case Symbol.EAN13:
                    case Symbol.POSTNET:
                    case Symbol.UPCA:
                    case Symbol.UPCE0:
                    case Symbol.UPCE1:
                    case Symbol.ISBT:
                    case Symbol.BPO:
                    case Symbol.CANPOST:
                    case Symbol.AUSPOST:
                    case Symbol.JAPOST:
                    case Symbol.PLANET:
                    case Symbol.DUTCHPOST:
                    case Symbol.TLCODE39:
                    case Symbol.TRIOPTIC:
                    case Symbol.CODE32:
                    case Symbol.COUPONCODE:
                    case Symbol.UPUIDTAG:
                    case Symbol.CODE4CB:
                        if (gType != typeof(SymFlagsOnly))
                            return Result.ERR_INVALID_PARAMETER;

                        pParms = Marshal.AllocHGlobal((int)pvSymStruct.length);

                        Marshal.StructureToPtr(pvSymStruct, pParms, false);

                        result = Native.hhpReadSymbologyConfig(cfgType, nSymbol, pParms);

                        if (Result.SUCCESS == result)
                        {
                            Marshal.PtrToStructure(pParms, pvSymStruct);
                        }

                        break;
                    case Symbol.OCR:
                        if (gType != typeof(SymCodeOCR))
                            return Result.ERR_INVALID_PARAMETER;

                        //int index = 0;

                        pParms = Marshal.AllocHGlobal((int)pvSymStruct.length);

                        Marshal.StructureToPtr(pvSymStruct, pParms, false);

                        result = Native.hhpReadSymbologyConfig(cfgType, nSymbol, pParms);

                        if (Result.SUCCESS == result)
                        {
                            Marshal.PtrToStructure(pParms, pvSymStruct);
                        }

                        break;
                    case Symbol.NUM_SYMBOLOGIES:
                        return Result.ERR_INVALID_PARAMETER;
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }

        /// <summary>
        /// Writes configuration items for a single symbology or for all symbologies. 
        /// Individual items to be written are specified by adding 
        /// the appropriate mask bit (OR it) to the mask member of the structure to which it belongs.
        /// Only items whose bits are set are written; all other items are ignored.
        /// </summary>
        /// <param name="nSymbol">One of the symbology enumerated types, e.g., SYMBOL.CODE39, SYMBOL.OCR, or SYMBOL.ALL to read all 
        /// symbologies.</param>
        /// <param name="pvSymStruct"> structure based on nSymbol</param>
        /// <returns></returns>
        public static Result WriteSymbologyConfig/*OK*/
        (
            [In]        Symbol nSymbol,
            [In]        ConfigBase pvSymStruct
         )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            if (pvSymStruct == null)
                return Result.ERR_INVALID_PARAMETER;

            Type gType = pvSymStruct.GetType();
            int type = 0;

            switch (nSymbol)
            {
                case Symbol.ALL:
                    if (gType != typeof(SymbologyParms))
                        return Result.ERR_INVALID_PARAMETER;
                    type = 0;
                    break;
                case Symbol.AZTEC:
                case Symbol.CODABAR:
                case Symbol.CODE11:
                case Symbol.CODE128:
                case Symbol.CODE39:
                case Symbol.CODE49:
                case Symbol.CODE93:
                case Symbol.COMPOSITE:
                case Symbol.DATAMATRIX:
                case Symbol.INT25:
                case Symbol.MAXICODE:
                case Symbol.MICROPDF:
                case Symbol.PDF417:
                case Symbol.QR:
                case Symbol.RSS:
                case Symbol.IATA25:
                case Symbol.CODABLOCK:
                case Symbol.MSI:
                case Symbol.MATRIX25:
                case Symbol.KORPOST:
                case Symbol.STRT25:
                case Symbol.PLESSEY:
                case Symbol.CHINAPOST:
                case Symbol.TELEPEN:
                case Symbol.CODE16K:
                case Symbol.POSICODE:
                    if (gType != typeof(SymFlagsRange))
                        return Result.ERR_INVALID_PARAMETER;
                    type = 1;
                    break;
                case Symbol.MESA:
                case Symbol.EAN8:
                case Symbol.EAN13:
                case Symbol.POSTNET:
                case Symbol.UPCA:
                case Symbol.UPCE0:
                case Symbol.UPCE1:
                case Symbol.ISBT:
                case Symbol.BPO:
                case Symbol.CANPOST:
                case Symbol.AUSPOST:
                case Symbol.JAPOST:
                case Symbol.PLANET:
                case Symbol.DUTCHPOST:
                case Symbol.TLCODE39:
                case Symbol.TRIOPTIC:
                case Symbol.CODE32:
                case Symbol.COUPONCODE:
                case Symbol.UPUIDTAG:
                case Symbol.CODE4CB:
                    if (gType != typeof(SymFlagsOnly))
                        return Result.ERR_INVALID_PARAMETER;
                    type = 2;
                    break;
                case Symbol.OCR:
                    if (gType != typeof(SymCodeOCR))
                        return Result.ERR_INVALID_PARAMETER;
                    type = 3;
                    break;
                case Symbol.NUM_SYMBOLOGIES:
                    return Result.ERR_INVALID_PARAMETER;
            }

            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                pParms = Marshal.AllocHGlobal((int)pvSymStruct.length);

                switch (type)
                {
                    case 0:
                        WriteAllSymbol(pParms, (SymbologyParms)pvSymStruct);
                        break;
                    case 1:
                    case 2:
                    case 3:
                        Marshal.StructureToPtr(pvSymStruct, pParms, false);
                        break;
                }


                result = Native.hhpWriteSymbologyConfig(nSymbol, pParms);


            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }

        /// <summary>
        /// Resets an individual symbology or all symbologies to their default values. 
        /// </summary>
        /// <param name="nSymId">One of the symbology enumerated types, e.g., SYMBOL.CODE39, SYMBOL.OCR, or SYMBOL.ALL to default 
        /// all symbologies.</param>
        /// <returns></returns>
        public static Result SetSymbologyDefaults/*OK*/
        (
            [In] Symbol nSymId
        )
        {
            return Native.hhpSetSymbologyDefaults(nSymId);
        }
        /// <summary>
        /// Enables/disables an individual symbology or all symbologies.
        /// </summary>
        /// <param name="nSymId">One of the symbology enumerated types, e.g., SYMBOL.CODE39, SYMBOL.OCR, or SYMBOL.ALL to enable/
        /// disable all symbologies.</param>
        /// <param name="bEnable">TRUE to enable symbology, FALSE to disable symbology.</param>
        /// <returns></returns>
        public static Result EnableDisableSymbology/*OK*/
        (
            [In] Symbol nSymId,
            [In] bool bEnable
        )
        {
            return Native.hhpEnableDisableSymbology(nSymId, bEnable);
        }
        /// <summary>
        /// Returns the specified symbology range maximum and minimum values.  If a symbology has no range values, the function returns 
        /// -1 for the minimum and maximum values.
        /// </summary>
        /// <param name="nSymId">The enumerated symbology types, eg., SYMBOL.CODE39, SYMBOL.PDF417, or SYMBOL.ALL to read the 
        /// max/min range for all symbologies.</param>
        /// <param name="nMinVals">A LONG pointer to hold the minimum range value for single symbologies, or a LONG array of size 
        /// NUM_SYMBOLOGIES if SYMBOL.ALL specified.  The min value will be -1 if the symbology does not 
        /// support a minimum length value.</param>
        /// <param name="nMaxVals">A LONG pointer to hold the maximum range value for single symbologies, or a LONG array of size 
        /// NUM_SYMBOLOGIES if SYMBOL.ALL specified.  The max value will be -1 if the symbology does not 
        /// support a maximum length value.</param>
        /// <returns></returns>
        public static Result ReadSymbologyRangeMaxMin/*OK*/
        (
            [In] Symbol nSymId,
            [In, Out] ref int[] nMinVals,
            [In, Out] ref int[] nMaxVals
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result result = Result.SUCCESS;

            try
            {
                if (nSymId == Symbol.ALL)
                {
                    if (nMinVals == null || nMaxVals == null || nMaxVals.Length != (int)Symbol.NUM_SYMBOLOGIES || nMinVals.Length != (int)Symbol.NUM_SYMBOLOGIES)
                        return Result.ERR_PARAMETER;
                }
                else
                {
                    if (nMinVals == null || nMaxVals == null || nMaxVals.Length != 1 || nMinVals.Length != 1)
                        return Result.ERR_PARAMETER;
                }

                result = Native.hhpReadSymbologyRangeMaxMin(nSymId, nMinVals, nMaxVals);
#if nouse
                fixed(int* pMin = nMinVals)
                fixed (int* pMax = nMaxVals)
                {
                    result = Native.hhpReadSymbologyRangeMaxMin(nSymId, pMin, pMax);
                }
#endif
            }
            catch (OutOfMemoryException)
            {
                return Result.ERR_MEMORY;
            }


            return result;

        }


        /// <summary>
        /// This function causes the imager to capture images and attempt to decode them.  Decoded data returned is translated by code 
        /// page and locale.  Barcode capture can be synchronous or asynchronous.  Synchronous capture is specified by setting the bWait 
        /// parameter CaptureBarcode to TRUE.  In this case, the function will not return until a barcode is read, an error occurs, or the 
        /// decode timeout is reached.  Asynchronous capture is specified by setting the bWait parameter CaptureBarcode to FALSE, 
        /// or whenever a barcode capture is initiated other than by the 5X80 SDK (e.g., from a hardware trigger).  In order to be notified of 
        /// an asynchronous transfer, you must enable at least one of the notification methods (see SetAsyncMethods on page 2-10). 
        /// </summary>
        /// <param name="pDecodeMsg">CFG_DECODE_MSG structure if bWait is TRUE. If bWait is FALSE, the parameter is ignored and should be NULL</param>
        /// <param name="dwTimeout">Maximum time (in milliseconds) to attempt to decode before declaring a no decode.  A value of 
        /// CURRENT_DECODE_TIMEOUT specifies that the timeout is whatever is currently set on the 
        /// imager.  A value of 0 indicates no timeout.</param>
        /// <param name="bWait">If TRUE, wait for capture to complete before returning.  If FALSE, one of the event notification 
        /// methods must be enabled to receive notification upon completion.</param>
        /// <returns></returns>
        public static Result CaptureBarcode
        (
            [In, Out]   DecodeMessage pDecodeMsg,
            [In]        UInt32 dwTimeout,
            [In]        bool bWait
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                if (pDecodeMsg == null)
                {
                    pParms = IntPtr.Zero;
                }
                else
                {
                    pParms = Marshal.AllocHGlobal(Marshal.SizeOf(pDecodeMsg));

                    Marshal.StructureToPtr(pDecodeMsg, pParms, false);
                }

                result = Native.hhpCaptureBarcode(pParms, dwTimeout, bWait);

                if (Result.SUCCESS == result && pParms != IntPtr.Zero)
                {
                    Marshal.PtrToStructure(pParms, pDecodeMsg);
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }
        /// <summary>
        /// This function causes the imager to capture images and attempt to decode them.  Decoded data returned is unmodified 8 bit ASCII 
        /// data.  Barcode capture can be synchronous or asynchronous.  Synchronous capture is specified by setting the bWait parameter 
        /// CaptureRawBarcode to TRUE.  In this case, the function will not return until a barcode is read, an error occurs, or the decode 
        /// timeout is reached.  Asynchronous capture is specified by setting the bWait parameter CaptureRawBarcode to FALSE, or 
        /// whenever a barcode capture is initiated other than by the 5X80 SDK (e.g., from a hardware trigger).  In order to be notified of an 
        /// asynchronous transfer, you must enable at least one of the notification methods (see SetAsyncMethods on page 2-10).
        /// </summary>
        /// <param name="pDecodeMsg">RAW_DECODE_MSG structure if bWait is TRUE.
        /// If bWait is FALSE, the parameter is ignored and should be NULL. </param>
        /// <param name="dwTimeout">Maximum time (in milliseconds) to attempt to decode before declaring a no decode.  A value of 
        /// CURRENT_DECODE_TIMEOUT specifies that the timeout is whatever is currently set on the 
        /// imager.  A value of 0 indicates no timeout.</param>
        /// <param name="bWait">If TRUE, wait for capture to complete before returning. If FALSE, one of the event notification 
        /// methods must be enabled to receive notification upon completion.</param>
        /// <returns></returns>
        public static Result CaptureRawBarcode/*OK*/
        (
            [In, Out]   RawDecodeMessage pDecodeMsg,
            [In]        UInt32 dwTimeout,
            [In]        bool bWait
        )
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result result = Result.SUCCESS;
            IntPtr pParms = IntPtr.Zero;

            try
            {
                if (pDecodeMsg == null)
                {
                    pParms = IntPtr.Zero;
                }
                else
                {
                    pParms = Marshal.AllocHGlobal(Marshal.SizeOf(pDecodeMsg));

                    Marshal.StructureToPtr(pDecodeMsg, pParms, false);
                }

                result = Native.hhpCaptureRawBarcode(pParms, dwTimeout, bWait);

                if (Result.SUCCESS == result && pParms != IntPtr.Zero)
                {
                    Marshal.PtrToStructure(pParms, pDecodeMsg);
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }

            if (IntPtr.Zero != pParms)
            {
                Marshal.FreeHGlobal(pParms);
            }

            return result;
        }
        /// <summary>
        /// This function changes the code page used when translating the decoded data from a string of bytes to Unicode.  The default 
        /// value is CP_ACP (ANSI code page).  There is no error checking on the values sent to this function, so you must determine 
        /// whether or not a code page is valid on the given system.
        /// </summary>
        /// <param name="dwCodePage">Code page to use when converting from BYTE string to Unicode.  The only 2 code pages that are 
        /// valid are CP_ACP and CP_OEMCP.     </param>
        /// <returns></returns>
        public static Result SetBarcodeDataCodePage
        (
            [In] CodePage dwCodePage
        )
        {
            return Native.hhpSetBarcodeDataCodePage(dwCodePage);
        }
        /// <summary>
        /// This function causes the imager to capture an image and transfer it to the host.  Values to be used from the structures are 
        /// specified by setting the appropriate bit mask for each item in the structure's mask member.
        /// </summary>
        /// <param name="pImg">IMAGE structure if bWait is TRUE.  If bWait is FALSE, the parameter is ignored 
        /// and should be NULL.    </param>
        /// <param name="pImgTrans">Optional pointer to an CFG_IMAGE_TRANSFER structure.  This structure overrides (just for this call) 
        /// the current imager configuration, and specifies the pixel subsample, cropping rectangle, transfer 
        /// compression type, compression factor (for JPEG lossy transfer), and progress notification method.  
        /// If this parameter is NULL, the current imager configuration settings are used except for the progress 
        /// notification methods that must be specified for each call if notification is desired. </param>
        /// <param name="pImgAcqu">Optional pointer to an CFG_IMAGE_ACQUISITION structure.  This structure overrides (just for this 
        /// call) the current imager configuration, to specify and configure the image capture method (type of 
        /// autoexposure control or manual mode). If this parameter is NULL, the current imager configuration 
        /// settings are used.</param>
        /// <param name="bWait">If TRUE, do not return until the image is received or an error occurs.  If FALSE, return immediately.  
        /// One of the event notification methods must be enabled to receive notification on completion.  (See 
        /// SetAsyncMethods on page 2-10.)</param>
        /// <returns></returns>
        public static Result AcquireImage(/*OK*/
            [In, Out] ImageMessage pImg,
            [In, Out] ImageTransferParms pImgTrans,
            [In, Out] ImageAcquisitionParms pImgAcqu,
            [In] bool bWait)
        {
            Result result = Result.SUCCESS;
            IntPtr pBufA = IntPtr.Zero;
            IntPtr pBufB = IntPtr.Zero;
            IntPtr pBufC = IntPtr.Zero;

            try
            {
                if (pImg != null)
                {
                    pBufA = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ImageMessage)));
                    Marshal.StructureToPtr(pImg, pBufA, false);
                }
                if (pImgTrans != null)
                {
                    pBufB = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ImageTransferParms)));
                    Marshal.StructureToPtr(pImgTrans, pBufB, false);
                }
                if (pImgAcqu != null)
                {
                    pBufC = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ImageAcquisitionParms)));
                    Marshal.StructureToPtr(pImgAcqu, pBufC, false);
                }
                result = Native.hhpAcquireImage(pBufA, pBufB, pBufC, bWait);
                if (result == Result.SUCCESS)
                {
                    if (pImg != null)
                    {
                        Marshal.PtrToStructure(pBufA, pImg);
                    }
                    if (pImgTrans != null)
                    {
                        Marshal.PtrToStructure(pBufB, pImgTrans);
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }
            catch (NotSupportedException)
            {
                result = Result.ERR_UNSUPPORTED;
            }
            catch (Exception)
            {
                result = Result.EOT;
            }
            if (pBufA != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufA);
            }
            if (pBufB != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufB);
            }
            if (pBufC != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufC);
            }

            return result;

        }
        /// <summary>
        /// This function causes the imager to transfer the last image captured to the host.  If bWait is TRUE, the function will not return until 
        /// the image is fully received or an error occurs.  If bWait is FALSE, the function returns immediately and you are notified when 
        /// image transfer has completed or an error has occurred.  pImgTrans is an optional parameter and can be NULL.   Setting the 
        /// appropriate bit mask for each item specifies active members of this structure.  This function can be used to obtain the image from 
        /// the last barcode capture attempt as well as the last image from an image capture attempt.
        /// </summary>
        /// <param name="pImg">IMAGE structure if bWait is TRUE.  If bWait is FALSE, the parameter is ignored 
        /// and should be NULL.    </param>
        /// <param name="pImgTrans">Optional pointer to an CFG_IMAGE_TRANSFER structure.  This structure overrides (just for this call) 
        /// the current imager configuration, and specifies the pixel subsample, cropping rectangle, transfer 
        /// compression type, compression factor (for JPEG lossy transfer), and progress notification method.  
        /// If this parameter is NULL, the current imager configuration settings are used except for the progress 
        /// notification methods that must be specified for each call if notification is desired. </param>
        /// <param name="bWait">If TRUE, do not return until the image is received or an error occurs.  If FALSE, return immediately.  
        /// One of the event notification methods must be enabled to receive notification on completion.</param>
        /// <returns></returns>
        public static Result GetLastImage(/*OK*/
            [In, Out] ImageMessage pImg,
            [In, Out] ImageTransferParms pImgTrans,
            [In] bool bWait)
        {
            Result result = Result.SUCCESS;
            IntPtr pBufA = IntPtr.Zero;
            IntPtr pBufB = IntPtr.Zero;
            try
            {
                if (pImg != null)
                {
                    pBufA = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ImageMessage)));
                    Marshal.StructureToPtr(pImg, pBufA, false);
                }
                if (pImgTrans != null)
                {
                    pBufB = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ImageTransferParms)));
                    Marshal.StructureToPtr(pImgTrans, pBufB, false);
                }
                result = Native.hhpGetLastImage(pBufA, pBufB, bWait);
                if (result == Result.SUCCESS)
                {
                    if (pImg != null)
                    {
                        Marshal.PtrToStructure(pBufA, pImg);
                    }
                    if (pImgTrans != null)
                    {
                        Marshal.PtrToStructure(pBufB, pImgTrans);
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }
            catch (NotSupportedException)
            {
                result = Result.ERR_UNSUPPORTED;
            }
            catch (Exception)
            {
                result = Result.EOT;
            }
            if (pBufA != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufA);
            }
            if (pBufB != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufB);
            }
            return result;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pIntelImg"></param>
        /// <param name="pDecodeMsg"></param>
        /// <param name="dwTimeout"></param>
        /// <param name="pImg"></param>
        /// <param name="bWait"></param>
        /// <returns></returns>
        private static Result AcquireIntelligentImage(/*not ok*/
            [In, Out] IntelligentImage pIntelImg,
            [In, Out] DecodeMessage pDecodeMsg,
            [In] UInt32 dwTimeout,
            [In, Out] ImageMessage pImg,
            [In] bool bWait)
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result result = Result.SUCCESS;
            IntPtr pBufA = IntPtr.Zero;
            IntPtr pBufB = IntPtr.Zero;
            IntPtr pBufC = IntPtr.Zero;

            try
            {
                if (pIntelImg != null)
                {
                    pBufA = Marshal.AllocHGlobal(Marshal.SizeOf(pIntelImg));

                    Marshal.StructureToPtr(pIntelImg, pBufA, false);
                }
                if (pDecodeMsg != null)
                {
                    pBufB = Marshal.AllocHGlobal(Marshal.SizeOf(pDecodeMsg));

                    Marshal.StructureToPtr(pDecodeMsg, pBufB, false);
                }
                if (pImg != null)
                {
                    pBufC = Marshal.AllocHGlobal(Marshal.SizeOf(pImg));

                    Marshal.StructureToPtr(pImg, pBufC, false);
                }

                result = Native.hhpAcquireIntelligentImage(pBufA, pBufB, dwTimeout, pBufC, bWait);

                if (Result.SUCCESS == result)
                {
                    if (pIntelImg != null)
                        Marshal.PtrToStructure(pBufA, pIntelImg);
                    if (pDecodeMsg != null)
                        Marshal.PtrToStructure(pBufB, pDecodeMsg);
                    if (pImg != null)
                        Marshal.PtrToStructure(pBufC, pImg);
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }
            catch (NotSupportedException)
            {
                result = Result.ERR_UNSUPPORTED;
            }
            catch (Exception)
            {
                result = Result.EOT;
            }
            if (pBufA != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufA);
            }
            if (pBufB != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufB);
            }
            if (pBufC != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufC);
            }

            return result;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pIntelImg"></param>
        /// <param name="pRawDecodeMsg"></param>
        /// <param name="dwTimeout"></param>
        /// <param name="pImg"></param>
        /// <param name="bWait"></param>
        /// <returns></returns>
        private static Result RawAcquireIntelligentImage(/*not ok*/
            [In, Out] IntelligentImage pIntelImg,
            [In, Out] RawDecodeMessage pRawDecodeMsg,
            [In] UInt32 dwTimeout,
            [In, Out] ImageMessage pImg,
            [In] bool bWait)
        {
            if (!EngineConnected())
                return Result.ERR_NOTCONNECTED;

            Result result = Result.SUCCESS;
            IntPtr pBufA = IntPtr.Zero;
            IntPtr pBufB = IntPtr.Zero;
            IntPtr pBufC = IntPtr.Zero;

            try
            {
                if (pIntelImg != null)
                {
                    pBufA = Marshal.AllocHGlobal(Marshal.SizeOf(pIntelImg));

                    Marshal.StructureToPtr(pIntelImg, pBufA, false);
                }
                if (pRawDecodeMsg != null)
                {
                    pBufB = Marshal.AllocHGlobal(Marshal.SizeOf(pRawDecodeMsg));

                    Marshal.StructureToPtr(pRawDecodeMsg, pBufB, false);
                }
                if (pImg != null)
                {
                    pBufC = Marshal.AllocHGlobal(Marshal.SizeOf(pImg));

                    Marshal.StructureToPtr(pImg, pBufC, false);
                }

                result = Native.hhpRawAcquireIntelligentImage(pBufA, pBufB, dwTimeout, pBufC, bWait);

                if (Result.SUCCESS == result)
                {
                    if (pIntelImg != null)
                        Marshal.PtrToStructure(pBufA, pIntelImg);
                    if (pRawDecodeMsg != null)
                        Marshal.PtrToStructure(pBufB, pRawDecodeMsg);
                    if (pImg != null)
                        Marshal.PtrToStructure(pBufC, pImg);
                }
            }
            catch (OutOfMemoryException)
            {
                result = Result.ERR_MEMORY;
            }
            catch (NotSupportedException)
            {
                result = Result.ERR_UNSUPPORTED;
            }
            catch (Exception)
            {
                result = Result.EOT;
            }
            if (pBufA != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufA);
            }
            if (pBufB != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufB);
            }
            if (pBufC != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBufC);
            }

            return result;
        }

        #region SendActionCommand
        /// <summary>
        /// This command allows the application to modify some of the imager hardware states.  The items that can be modified include 
        /// turning the illumination LEDs on/off, turning the aimer LEDs on/off, or causing the device's beeper to beep/double beep.
        /// </summary>
        /// <param name="actionCmd">One of the values of enum ACTION (ACTION.AIMER_CMD, ACTION.ILLUMINATION_CMD, or 
        /// ACTION.BEEP_CMD).  </param>
        /// <param name="nVal">ON_OFF.ON/ON_OFF.OFF for illumination or aimers.
        /// BEEP.SINGLE_BEEP/BEEP.DOUBLE_BEEP for beeper.  </param>
        /// <returns></returns>
        [Obsolete("This function will not use in fature, please use TurnOnAimerLed/TurnOffAimerLed/TurnOnIlluminationLed/TurnOffIlluminationLed/SetBeeper instead.")]
        public static Result SendActionCommand
        (
            [In] Action actionCmd,
            [In] int nVal
        )
        {
            return Native.hhpSendActionCommand(actionCmd, nVal);
        }
        /// <summary>
        /// Turning the aimer LEDs on
        /// </summary>
        /// <returns></returns>
        public static Result TurnOnAimerLed()
        {
            return Native.hhpSendActionCommand(Action.AIMER_CMD, (int)Led.ON);
        }
        /// <summary>
        /// Turning the aimer LEDs off
        /// </summary>
        /// <returns></returns>
        public static Result TurnOffAimerLed()
        {
            return Native.hhpSendActionCommand(Action.AIMER_CMD, (int)Led.OFF);
        }
        /// <summary>
        /// Turning the illumination LEDs on
        /// </summary>
        /// <returns></returns>
        public static Result TurnOnIlluminationLed()
        {
            return Native.hhpSendActionCommand(Action.ILLUMINATION_CMD, (int)Led.ON);
        }
        /// <summary>
        /// Turning the illumination LEDs off
        /// </summary>
        /// <returns></returns>
        public static Result TurnOffIlluminationLed()
        {
            return Native.hhpSendActionCommand(Action.ILLUMINATION_CMD, (int)Led.OFF);
        }
        /// <summary>
        /// causing the device's beeper to beep/double beep.
        /// </summary>
        /// <param name="beep"></param>
        /// <returns></returns>
        public static Result SetBeeper(Beep beep)
        {
            return Native.hhpSendActionCommand(Action.BEEP_CMD, (int)beep);
        }
        #endregion

        /// <summary>
        /// The 5X80 SDK provides the ability to update the firmware application running on the imager.  UpgradeFirmware checks the 
        /// file contents to verify that it is a firmware application file before the file is downloaded to the imager.   The firmware file is 
        /// transferred to the imager compressed (lossless) unless the SDK has determined that the imager is running in bootstrap code 
        /// instead of the current firmware application.  In this case, the file is transferred uncompressed.  This function only supports 
        /// synchronous operation, so it does not return until the firmware file has been transferred to the imager and the imager has burned 
        /// the new code into flash memory.  When this function returns, the connection (host COM port) is connected at the default baud 
        /// rate of 115200.
        /// </summary>
        /// <param name="ptcFirmwareFilename">String containing the fully qualified filename of the file that contains the code to be sent to the 
        /// imager.  The file extension is usually .bin or .axf.  The file is sent using an Hmodem, which is a 
        /// derivative of Xmodem 1K.</param>
        /// <param name="pdwTransferPercent">Pointer to a DWORD that contains the current percent transferred value(0 to 100).  If 
        /// pdwTransferPercent is valid, the transfer completion percent is written to it.   This is updated after 
        /// each packet is sent. </param>
        /// <param name="hTransferNotifyHwnd">Handle to the window that is to receive the transfer update messages.  The message is sent 
        /// when the percentage changes by more than 1%.  The window associated with the handle should 
        /// hook the WM_ID.WM_SDK_PROGRESS_HWND_MSG message.   
        /// After file transfer is complete and while the imager is storing the new code in flash, the message 
        /// WM_ID.WM_SDK_IMAGER_FLASHING is sent to the window HWND (if valid).   The parameters are:
        /// WPARAM - Bytes transferred so far
        /// LPARAM - Bytes to be sent
        /// The window should also hook the WM_ID.WM_SDK_IMAGER_FLASHING message.  The parameters 
        /// sent are: 
        /// 1st Call wParam=1  lParam=0 transfer is done and flashing has begun
        /// Subsequent wParam=x  lParam=1 Where x toggles between 0 and 1 every ½ second
        /// Final Call wParam=0  lParam=0 Flashing is complete</param>
        /// <returns></returns>
        static Result UpgradeFirmware
        (
            [In]        string ptcFirmwareFilename,
            [In, Out]   IntPtr pdwTransferPercent,
            [In, Out]   IntPtr hTransferNotifyHwnd
        )
        {
            return Native.hhpUpgradeFirmware(ptcFirmwareFilename, pdwTransferPercent, hTransferNotifyHwnd);
        }
        /// <summary>
        /// The SDK API provides the ability to provide OEM device-dependent extensions to support the imager hardware sleep lines, and 
        /// hardware trigger and/or special COM port driver configuration/initialization.  The definitions and function prototype are located in 
        /// the header file OemDll.h.  Also see OEM-Configurable SDK Functionality on page 5-1.
        /// </summary>
        /// <param name="ptcHwrFilename"></param>
        /// <returns></returns>
        static Result SetHardwareLineDllFileName
        (
            [In] String ptcHwrFilename
        )
        {
            return Native.hhpSetHardwareLineDllFileName(ptcHwrFilename);
        }

        #region Start scan

        /*public static RESULT AsynCapture(uint timeout)
        {
            RESULT nResult = RESULT.INITIALIZE;  // Return code.
            IntPtr hWnd = GetForegroundWindow(); // The window to which message is to be sent. (note: this example is MFC c++)
            // Register the message window with the SDK. 
            if ((nResult = Linkage.SetAsyncMethods(IntPtr.Zero, hWnd, null)) == RESULT.SUCCESS)
            {
                // Call the SDK function to capture a barcode, 6 second timeout.  Unless call fails, you will get a message when command completes.
                nResult = Linkage.CaptureBarcode(null, timeout, BOOL.FALSE);
            }
            return nResult;
        }*/

        #endregion


        #region private function

        private static void WriteAllConfigStructures([In, Out] IntPtr pParms, [In] AllConfigParms pStruct)
        {
            int index = 4;

            Marshal.WriteInt32(pParms, pStruct.length); // first structure size

            StructureToPtr(pParms, ref index, pStruct.beeperCfg);
            StructureToPtr(pParms, ref index, pStruct.triggerCfg);
            StructureToPtr(pParms, ref index, pStruct.decoderCfg);
            StructureToPtr(pParms, ref index, pStruct.powerCfg);
            StructureToPtr(pParms, ref index, pStruct.versionInfo);

            index += WriteAllSymbol(new IntPtr(pParms.ToInt64() + index), pStruct.symbolCfg);

            StructureToPtr(pParms, ref index, pStruct.imgAcqu);
            StructureToPtr(pParms, ref index, pStruct.imgTrans);

            WriteInt32(pParms, ref index, pStruct.sequenceCfg.length);

            WriteInt32(pParms, ref index, (int)pStruct.sequenceCfg.dwMask);
            WriteInt32(pParms, ref index, (int)pStruct.sequenceCfg.sequenceMode);
            WriteInt32(pParms, ref index, (int)pStruct.sequenceCfg.dwNumBarCodes);

            for (int i = 0; i < pStruct.sequenceCfg.seqBarCodes.Length; i++)
            {
                StructureToPtr(pParms, ref index, pStruct.sequenceCfg.seqBarCodes[i]);
            }
        }

        private static void ReadAllConfigStructures([In, Out] IntPtr pParms, [In] AllConfigParms pStruct)
        {
            int index = 4;

            PtrToStructure(pParms, ref index, pStruct.beeperCfg);
            PtrToStructure(pParms, ref index, pStruct.triggerCfg);
            PtrToStructure(pParms, ref index, pStruct.decoderCfg);
            PtrToStructure(pParms, ref index, pStruct.powerCfg);
            PtrToStructure(pParms, ref index, pStruct.versionInfo);

            index += ReadAllSymbol(new IntPtr(pParms.ToInt64() + index), pStruct.symbolCfg);

            PtrToStructure(pParms, ref index, pStruct.imgAcqu);
            PtrToStructure(pParms, ref index, pStruct.imgTrans);

            index += 4; // skip struct size
            uint mask = 0;
            ReadInt32(pParms, ref index, ref mask);
            pStruct.sequenceCfg.dwMask = (SequenceMask)mask;

            uint mode = 0;
            ReadInt32(pParms, ref index, ref mode);
            pStruct.sequenceCfg.sequenceMode = (SequenceMode)mode;

            ReadInt32(pParms, ref index, ref pStruct.sequenceCfg.dwNumBarCodes);

            for (int i = 0; i < pStruct.sequenceCfg.seqBarCodes.Length; i++)
            {
                SequenceBarcodeParms tmp = new SequenceBarcodeParms();
                PtrToStructure(pParms, ref index, tmp); 
                pStruct.sequenceCfg.seqBarCodes[i] = tmp;
            }
        }

        private static int ReadAllSymbol([In] IntPtr pParms, [In, Out] SymbologyParms pvSymStruct)
        {
            int index = 4; // Skill strcutre size

            PtrToStructure(pParms, ref index, pvSymStruct.codabar);
            PtrToStructure(pParms, ref index, pvSymStruct.code11);
            PtrToStructure(pParms, ref index, pvSymStruct.code128);
            PtrToStructure(pParms, ref index, pvSymStruct.code39);
            PtrToStructure(pParms, ref index, pvSymStruct.code49);
            PtrToStructure(pParms, ref index, pvSymStruct.code93);
            PtrToStructure(pParms, ref index, pvSymStruct.composite);
            PtrToStructure(pParms, ref index, pvSymStruct.datamatrix);
            PtrToStructure(pParms, ref index, pvSymStruct.ean8);
            PtrToStructure(pParms, ref index, pvSymStruct.ean13);
            PtrToStructure(pParms, ref index, pvSymStruct.iata25);
            PtrToStructure(pParms, ref index, pvSymStruct.int2of5);
            PtrToStructure(pParms, ref index, pvSymStruct.isbt);
            PtrToStructure(pParms, ref index, pvSymStruct.msi);
            PtrToStructure(pParms, ref index, pvSymStruct.upcA);
            PtrToStructure(pParms, ref index, pvSymStruct.upcE);
            PtrToStructure(pParms, ref index, pvSymStruct.australiaPost);
            PtrToStructure(pParms, ref index, pvSymStruct.britishPost);
            PtrToStructure(pParms, ref index, pvSymStruct.canadaPost);
            PtrToStructure(pParms, ref index, pvSymStruct.dutchPost);
            PtrToStructure(pParms, ref index, pvSymStruct.japanPost);
            PtrToStructure(pParms, ref index, pvSymStruct.usPlanet);
            PtrToStructure(pParms, ref index, pvSymStruct.usPostnet);
            PtrToStructure(pParms, ref index, pvSymStruct.aztec);
            PtrToStructure(pParms, ref index, pvSymStruct.aztecMesa);
            PtrToStructure(pParms, ref index, pvSymStruct.codablock);
            PtrToStructure(pParms, ref index, pvSymStruct.maxicode);
            PtrToStructure(pParms, ref index, pvSymStruct.microPDF);
            PtrToStructure(pParms, ref index, pvSymStruct.pdf417);
            PtrToStructure(pParms, ref index, pvSymStruct.qr);
            PtrToStructure(pParms, ref index, pvSymStruct.rss);
            PtrToStructure(pParms, ref index, pvSymStruct.tlCode39);
            PtrToStructure(pParms, ref index, pvSymStruct.ocr);
            PtrToStructure(pParms, ref index, pvSymStruct.matrix25);
            PtrToStructure(pParms, ref index, pvSymStruct.koreaPost);
            PtrToStructure(pParms, ref index, pvSymStruct.triopticCode);
            PtrToStructure(pParms, ref index, pvSymStruct.code32);
            PtrToStructure(pParms, ref index, pvSymStruct.code2of5);
            PtrToStructure(pParms, ref index, pvSymStruct.plesseyCode);
            PtrToStructure(pParms, ref index, pvSymStruct.chinaPost);
            PtrToStructure(pParms, ref index, pvSymStruct.telepen);
            PtrToStructure(pParms, ref index, pvSymStruct.code16k);
            PtrToStructure(pParms, ref index, pvSymStruct.posiCode);
            PtrToStructure(pParms, ref index, pvSymStruct.couponCode);
            PtrToStructure(pParms, ref index, pvSymStruct.upuIdTag);
            PtrToStructure(pParms, ref index, pvSymStruct.code4CB);

            return index;
        }

        private static int WriteAllSymbol([In, Out] IntPtr pParms, [In] SymbologyParms pvSymStruct)
        {
            int index = 0;

            WriteInt32(pParms, ref index, pvSymStruct.length);// structures size

            StructureToPtr(pParms, ref index, pvSymStruct.codabar);
            StructureToPtr(pParms, ref index, pvSymStruct.code11);
            StructureToPtr(pParms, ref index, pvSymStruct.code128);
            StructureToPtr(pParms, ref index, pvSymStruct.code39);
            StructureToPtr(pParms, ref index, pvSymStruct.code49);
            StructureToPtr(pParms, ref index, pvSymStruct.code93);
            StructureToPtr(pParms, ref index, pvSymStruct.composite);
            StructureToPtr(pParms, ref index, pvSymStruct.datamatrix);
            StructureToPtr(pParms, ref index, pvSymStruct.ean8);
            StructureToPtr(pParms, ref index, pvSymStruct.ean13);
            StructureToPtr(pParms, ref index, pvSymStruct.iata25);
            StructureToPtr(pParms, ref index, pvSymStruct.int2of5);
            StructureToPtr(pParms, ref index, pvSymStruct.isbt);
            StructureToPtr(pParms, ref index, pvSymStruct.msi);
            StructureToPtr(pParms, ref index, pvSymStruct.upcA);
            StructureToPtr(pParms, ref index, pvSymStruct.upcE);
            StructureToPtr(pParms, ref index, pvSymStruct.australiaPost);
            StructureToPtr(pParms, ref index, pvSymStruct.britishPost);
            StructureToPtr(pParms, ref index, pvSymStruct.canadaPost);
            StructureToPtr(pParms, ref index, pvSymStruct.dutchPost);
            StructureToPtr(pParms, ref index, pvSymStruct.japanPost);
            StructureToPtr(pParms, ref index, pvSymStruct.usPlanet);
            StructureToPtr(pParms, ref index, pvSymStruct.usPostnet);
            StructureToPtr(pParms, ref index, pvSymStruct.aztec);
            StructureToPtr(pParms, ref index, pvSymStruct.aztecMesa);
            StructureToPtr(pParms, ref index, pvSymStruct.codablock);
            StructureToPtr(pParms, ref index, pvSymStruct.maxicode);
            StructureToPtr(pParms, ref index, pvSymStruct.microPDF);
            StructureToPtr(pParms, ref index, pvSymStruct.pdf417);
            StructureToPtr(pParms, ref index, pvSymStruct.qr);
            StructureToPtr(pParms, ref index, pvSymStruct.rss);
            StructureToPtr(pParms, ref index, pvSymStruct.tlCode39);
            StructureToPtr(pParms, ref index, pvSymStruct.ocr);
            StructureToPtr(pParms, ref index, pvSymStruct.matrix25);
            StructureToPtr(pParms, ref index, pvSymStruct.koreaPost);
            StructureToPtr(pParms, ref index, pvSymStruct.triopticCode);
            StructureToPtr(pParms, ref index, pvSymStruct.code32);
            StructureToPtr(pParms, ref index, pvSymStruct.code2of5);
            StructureToPtr(pParms, ref index, pvSymStruct.plesseyCode);
            StructureToPtr(pParms, ref index, pvSymStruct.chinaPost);
            StructureToPtr(pParms, ref index, pvSymStruct.telepen);
            StructureToPtr(pParms, ref index, pvSymStruct.code16k);
            StructureToPtr(pParms, ref index, pvSymStruct.posiCode);
            StructureToPtr(pParms, ref index, pvSymStruct.couponCode);
            StructureToPtr(pParms, ref index, pvSymStruct.upuIdTag);
            StructureToPtr(pParms, ref index, pvSymStruct.code4CB);

            return index;
        }

        private static void PtrToStructure(
            [In] IntPtr ptr, 
            [In, Out] ref int index,
            [In, Out] object struc)
        {
            Marshal.PtrToStructure(new IntPtr(ptr.ToInt64() + index), struc);
            index += Marshal.SizeOf(struc);
        }

        private static void StructureToPtr(
            [In, Out] IntPtr ptr,
            [In, Out] ref int index, 
            [In] object struc)
        {
            Marshal.StructureToPtr(struc, new IntPtr(ptr.ToInt64() + index), false);
            index += Marshal.SizeOf(struc);
        }

        private static void WriteInt32(
            [In, Out] IntPtr ptr,
            [In, Out] ref int index,
            [In] int value)
        {
            Marshal.WriteInt32(new IntPtr(ptr.ToInt64() + index), value);
            index += Marshal.SizeOf(value);
        }

        private static void ReadInt32(
            [In] IntPtr ptr,
            [In, Out] ref int index,
            [In, Out] ref uint value)
        {
            value = (uint)Marshal.ReadInt32(new IntPtr(ptr.ToInt64() + index));
            index += Marshal.SizeOf(value);
        }
        private static void ReadInt32(
            [In] IntPtr ptr,
            [In, Out] ref int index,
            [In, Out] ref int value)
        {
            value = Marshal.ReadInt32(new IntPtr(ptr.ToInt64() + index));
            index += Marshal.SizeOf(value);
        }
        #endregion
#if WindowsCE
        [DllImport("coredll.dll")]
#else
        [DllImport("kernel32.dll")]
#endif
        private static extern bool GetExitCodeThread(UInt32 hThread, out uint lpExitCode);

#if WindowsCE
        [DllImport("coredll.dll")]
#else
        [DllImport("kernel32.dll")]
#endif
        private static extern uint GetLastError();

        static bool IsAlive()
        {
            if (mCaptureThread != null)
            {
#if WindowsCE
                uint exCode = 0;
                if (!GetExitCodeThread((uint)mCaptureThread.ManagedThreadId, out exCode))
                {
//                    uint errorcode = GetLastError();
//                    return (errorcode == 10000U);
                    return false;
                }
                return (exCode == 0x00000103);

#else
                return g_hWndThread.IsAlive;
#endif
            }
            return false;
        }
    }
}
