using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using KSP.IO;
using UnityEngine;

using KerbalSimpit.IO.Ports;

namespace KerbalSimpit.Serial
{
    /* KSPSerialPort
       This class includes a threadsafe queue implementation based on
       https://stackoverflow.com/questions/12375339/threadsafe-fifo-queue-buffer
    */

    public class KSPSerialPort
    {
        public string PortName;
        private int BaudRate;
        public  byte ID;

        private readonly object queueLock = new object();
        private Queue<byte[]> packetQueue = new Queue<byte[]>();

        private SerialPort Port;
    
        // Header bytes are alternating ones and zeroes, with the exception
        // of encoding the protocol version in the final four bytes.
        private readonly byte[] PacketHeader = { 0xAA, 0x50 };

        // Packet buffer related fields
        // This is *total* packet size, including all headers.
        private const int MaxPacketSize = 32;
        // Buffer for sending outbound packets
        private byte[] OutboundPacketBuffer;
        private enum ReceiveStates: byte {
            HEADER1, // Waiting for first header byte
            HEADER2, // Waiting for second header byte
            SIZE,    // Waiting for payload size
            TYPE,    // Waiting for packet type
            PAYLOAD  // Waiting for payload packets
        }
        // Serial worker uses these to buffer inbound data
        private ReceiveStates CurrentState;
        private byte CurrentPayloadSize;
        private byte CurrentPayloadType;
        private byte CurrentBytesRead;
        private byte[] PayloadBuffer = new byte[255];
        // Semaphore to indicate whether the reader worker should do work
        private volatile bool DoSerial;
        private Thread SerialReadThread, SerialWriteThread;

        // If we're opening a COM serial port, assume we're running on Windows.
        private bool isWindows = false;

        // Constructors:
        // pn: port number
        // br: baud rate
        // idx: a unique identifier for this port
        public KSPSerialPort(string pn, int br): this(pn, br, 37, false)
        {
        }
        public KSPSerialPort(string pn, int br, byte idx): this(pn, br, idx, false)
        {
        }
        public KSPSerialPort(string pn, int br, byte idx, bool vb)
        {
            PortName = pn;
            BaudRate = br;
            ID = idx;

            DoSerial = false;
            // Note that we initialise the packet buffer once, and reuse it.
            // I don't know if that's acceptable C# or not.
            // But I hope it's faster.
            OutboundPacketBuffer = new byte[MaxPacketSize];
            Array.Copy(PacketHeader, OutboundPacketBuffer, PacketHeader.Length);

            Port = new SerialPort(PortName, BaudRate, Parity.None,
                                  8, StopBits.One);

            if (System.Text.RegularExpressions.Regex.IsMatch(pn, "^COM[0-9]?",
                                                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                isWindows = true;
                if (KSPit.Config.Verbose)
                    Debug.Log(String.Format("KerbalSimpit: Using serial polling thread for {0}", pn));
            } else {
                if (KSPit.Config.Verbose)
                    Debug.Log(String.Format("KerbalSimpit: Using async reader thread for {0}", pn));
            }
        }

        // Open the serial port
        public bool open() {
            if (!Port.IsOpen)
            {
                try
                {
                    Port.Open();
                    SerialWriteThread = new Thread(SerialWriteQueueRunner);
                    if (isWindows)
                    {
                        SerialReadThread = new Thread(SerialPollingWorker);
                    } else {
                        SerialReadThread = new Thread(AsyncReaderWorker);
                    }
                    DoSerial = true;
                    SerialReadThread.Start();
                    SerialWriteThread.Start();
                    while (!SerialReadThread.IsAlive || !SerialWriteThread.IsAlive);
                }
                catch (Exception e)
                {
                    Debug.Log(String.Format("KerbalSimpit: Error opening serial port {0}: {1}", PortName, e.Message));
                }
            }
            return Port.IsOpen;
        }

        // Close the serial port
        public void close() {
            if (Port.IsOpen)
            {
                DoSerial = false;
                Thread.Sleep(500);
                Port.Close();
            }
        }

        // Construct a KerbalSimpit packet, and enqueue it.
        // Note that callers of this method are rarely in the main
        // game thread, hence using a threadsafe queue implementation.
        public void sendPacket(byte Type, object Data)
        {
            KSPit.checkWatchdog(ID);
            // Note that header sizes are hardcoded here:
            // packet[0] = first byte of header
            // packet[1] = second byte of header
            // packet[2] = payload size
            // packet[3] = packet type
            // packet[4-x] = packet payload
            byte[] buf;
            if (Data.GetType().Name == "Byte[]")
            {
                buf = (byte[])Data;
            } else {
                buf = ObjectToByteArray(Data);
            }
            byte PayloadSize = (byte)Math.Min(buf.Length, (MaxPacketSize-4));
            // Hopefully just using the length of the array is enough and
            // we don't need this any more. Fallback: Put it in the first
            // byte of the outbountpacketbuffer.
            //byte PacketSize = (byte)(PayloadSize + 4);
            OutboundPacketBuffer[2] = PayloadSize;
            OutboundPacketBuffer[3] = Type;
            Array.Copy(buf, 0, OutboundPacketBuffer, 4, PayloadSize);
            lock(queueLock)
            {
                packetQueue.Enqueue(OutboundPacketBuffer);
                Monitor.PulseAll(queueLock);
            }
        }

        public void DataReceivedEventHandler(object sender, SerialDataReceivedEventArgs args)
        {
            byte[] buffer = new byte[MaxPacketSize];
            int idx = 0;
            while (Port.BytesToRead > 0 && idx < MaxPacketSize)
            {
                buffer[idx] = (byte)Port.ReadByte();
                idx++;
            }
            ReceivedDataEvent(buffer, idx);
        }

        // Send arbitrary data. Shouldn't be used.
        private void sendData(object data)
        {
            byte[] buf = ObjectToByteArray(data);
            if (buf != null && Port.IsOpen)
            {
                Port.Write(buf, 0, buf.Length);
            }
        }

        // Convert the given object to an array of bytes
        private byte[] ObjectToByteArray(object obj)
        {
            int len;
            Type objType = obj.GetType();
            if (objType.IsArray)
            {
                // The Cast method here is from Linq.
                // TODO: Find a better way to do this.
                // If you're in here, len is correctly calculated but
                // right now we only send len bytes of 0x00.
                // TODO: Fix what we're sending.
                object[] objarr = ((Array)obj).Cast<object>().ToArray();
                len = objarr.Length * Marshal.SizeOf(objType.GetElementType());
            } else
            {
                len = Marshal.SizeOf(obj);
            }
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, arr, 0, len);
            Marshal.FreeHGlobal(ptr);
            int newlen = arr.Length;
            return arr;
        }

        private void SerialWriteQueueRunner()
        {
            Action SerialWrite = null;
            SerialWrite = delegate {
                byte[] dequeued = null;
                lock(queueLock)
                {
                    // If the queue is empty and serial is still running,
                    // use Monitor to wait until we're told it changed.
                    if (packetQueue.Count == 0)
                    {
                        Monitor.Wait(queueLock);
                    }

                    // Check if there's anything in the queue.
                    // Note that the queue might still be empty if we
                    // were waiting and serial has stopped.
                    if (packetQueue.Count > 0)
                    {
                        dequeued = packetQueue.Dequeue();
                    }
                }
                if (dequeued != null && Port.IsOpen)
                {
                    Port.Write(dequeued, 0, dequeued.Length);
                    dequeued = null;
                }
            };
            Debug.Log(String.Format("KerbalSimpit: Starting write thread for port {0}", PortName));
            while (DoSerial)
            {
                SerialWrite();
            }
            Debug.Log(String.Format("KerbalSimpit: Write thread for port {0} exiting.", PortName));
        }

        private void SerialPollingWorker()
        {
            Action SerialRead = null;
            SerialRead = delegate {
                try
                {
                    int actualLength = Port.BytesToRead;
                    if (actualLength > 0)
                    {
                        byte[] received = new byte[actualLength];
                        Port.Read(received, 0, actualLength);
                        ReceivedDataEvent(received, actualLength);
                    }
                }
                catch(System.IO.IOException exc)
                {
                    Debug.Log(String.Format("KerbalSimpit: IOException in serial worker for {0}: {1}", PortName, exc.ToString()));
                }
                Thread.Sleep(10); // TODO: Tune this.
            };
            Debug.Log(String.Format("KerbalSimpit: Starting poll thread for port {0}", PortName));
            while (DoSerial)
            {
                SerialRead();
            }
            Debug.Log(String.Format("KerbalSimpit: Poll thread for port {0} exiting.", PortName));
        }

        // This method spawns a new thread to read data from the serial connection
        private void AsyncReaderWorker()
        {
            byte[] buffer = new byte[MaxPacketSize];
            Action SerialRead = null;
            SerialRead = delegate {
                try
                {
                    Port.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate(IAsyncResult ar) {
                            try
                            {
                                int actualLength = Port.BaseStream.EndRead(ar);
                                byte[] received = new byte[actualLength];
                                Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                                ReceivedDataEvent(received, actualLength);
                            }
                            catch(System.IO.IOException exc)
                            {
                                Debug.Log(String.Format("KerbalSimpit: IOException in serial worker for {0}: {1}", PortName, exc.ToString()));
                            }
                        }, null);
                }
                catch (InvalidOperationException)
                {
                    Debug.Log(String.Format("KerbalSimpit: Trying to read port {0} that isn't open, sleeping", PortName));
                    Thread.Sleep(500);
                }
            };
            Debug.Log(String.Format("KerbalSimpit: Starting async read thread for port {0}", PortName));
            while (DoSerial)
            {
                SerialRead();
            }
            Debug.Log(String.Format("KerbalSimpit: async read thread for port {0} exiting.", PortName));
        }

        // Handle data read in worker thread
        private void ReceivedDataEvent(byte[] ReadBuffer, int BufferLength)
        {
            for (int x=0; x<BufferLength; x++)
            {
                switch(CurrentState)
                {
                    case ReceiveStates.HEADER1:
                        if (ReadBuffer[x] == PacketHeader[0])
                        {
                            CurrentState = ReceiveStates.HEADER2;
                        }
                        break;
                    case ReceiveStates.HEADER2:
                        if (ReadBuffer[x] == PacketHeader[1])
                        {
                            CurrentState = ReceiveStates.SIZE;
                        } else
                        {
                            CurrentState = ReceiveStates.HEADER1;
                        }
                        break;
                    case ReceiveStates.SIZE:
                        CurrentPayloadSize = ReadBuffer[x];
                        CurrentState = ReceiveStates.TYPE;
                        break;
                    case ReceiveStates.TYPE:
                        CurrentPayloadType = ReadBuffer[x];
                        CurrentBytesRead = 0;
                        CurrentState = ReceiveStates.PAYLOAD;
                        break;
                    case ReceiveStates.PAYLOAD:
                        PayloadBuffer[CurrentBytesRead] = ReadBuffer[x];
                        CurrentBytesRead++;
                        if (CurrentBytesRead == CurrentPayloadSize)
                        {
                            OnPacketReceived(CurrentPayloadType, PayloadBuffer,
                                             CurrentBytesRead);
                            CurrentState = ReceiveStates.HEADER1;
                        }
                        break;
                }
            }
        }

        private void OnPacketReceived(byte Type, byte[] Payload, byte Size)
        {
            byte[] buf = new byte[Size];
            Array.Copy(Payload, buf, Size);

            KSPit.onSerialReceivedArray[Type].Fire(ID, buf);
        }
    }
}
