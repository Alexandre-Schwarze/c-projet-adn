﻿using NodeNet.GUI.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeNet.Network
{

    public class ConnectionManager : INotifyPropertyChanged
    {

        public class StateObject
        {
            // Client socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 4096;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
        }

        //private Protocol protocol = new Protocol();

        private List<Socket> sockets = new List<Socket>();
        private Socket socket { get; set; }
        private string msg;
        private static ManualResetEvent connectDone = new ManualResetEvent(false);
        private static ManualResetEvent sendDone = new ManualResetEvent(false);
        private static ManualResetEvent receiveDone = new ManualResetEvent(false);

        private MainViewModel mvm { get; set; }
        private void RaisePropertyChanged(String property)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
        public event PropertyChangedEventHandler PropertyChanged;

        public String message
        {
            get { return msg; }
            set { msg = value; RaisePropertyChanged("message"); Console.WriteLine("Message Raised"); }
        }

        public List<Socket> Sockets
        {
            get { return sockets; }
            set { sockets = value; RaisePropertyChanged("Sockets"); Console.WriteLine("Socket Raised"); }
        }



        #region Ctor
        public ConnectionManager(MainViewModel mvm)
        {
            this.mvm = mvm;
        }
        #endregion

        #region Méthodes client/serveur

        public async Task StartServerAsync(IPAddress ip, int port)
        {
            Console.WriteLine(" TEST "+this.ToString());
            TcpListener listener = new TcpListener(ip, port);
            listener.Start();

            while (true)
            {
                this.sockets.Add(await listener.AcceptSocketAsync());
                Console.WriteLine(String.Format("Client Connection accepted from {0}", sockets.Last().RemoteEndPoint.ToString()));
                Receive(sockets.Last());
            }
        }

        public async void StartClient(IPAddress ip, int port)
        {
            IPEndPoint remoteEP = new IPEndPoint(ip, port);
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), socket);

                connectDone.WaitOne();

                Receive(socket);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        #endregion

        #region Tools

        /// <summary>
        /// Compresse un tableau d'octets vers un nouveau tableau d'octets.
        /// </summary>
        public static byte[] Compress(byte[] raw)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory,
                    CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
            }
        }

        /// <summary>
        /// Decompresse un tableau d'octets vers un nouveau tableau d'octets.
        /// </summary>
        static byte[] Decompress(byte[] gzip)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }

        private byte[] Serialize(object obj)
        {
            if (obj == null)
                return null;

            BinaryFormatter bf = new BinaryFormatter();
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    bf.Serialize(ms, obj);
                    Console.WriteLine("Serialized Array Length : " + ms.Length);
                    byte[] compressed = Compress(ms.ToArray());
                    Console.WriteLine("Serialized and Compressed Array Length : " + compressed.Length);
                    return compressed;
                }
            }
            catch (SerializationException ex)
            {
                Console.WriteLine("Serialize Error : " + ex);
                return null;
            }
        }

        private T Deserialize<T>(byte[] data)
        {
            object obj = null;
            try
            {
                byte[] uncompressed = Decompress(data);
                BinaryFormatter bf = new BinaryFormatter();
                using (MemoryStream ms = new MemoryStream(uncompressed))
                {
                    obj = bf.Deserialize(ms);
                }
            }
            catch (SerializationException ex)
            {
                Console.WriteLine("Deserialize Error : " + ex);
            }

            return (T)obj;
        }

        #endregion

        #region Méthodes envoi/réception
        private void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        public void Send(object obj)
        {        
            byte[] data = Serialize(obj);

            foreach (Socket socket in sockets)
            {
                try
                {
                    socket.BeginSend(data, 0, data.Length, 0,
                        new AsyncCallback(SendCallback), socket);
                }
                catch (SocketException ex)
                {
                    /// Client Down ///
                    if (!socket.Connected)
                        Console.WriteLine("Client " + socket.RemoteEndPoint.ToString() + " Disconnected");
                    Console.WriteLine(ex.ToString());
                }
            }
        }
        #endregion

        #region Méthodes Callback
        private void ReceiveCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the client socket 
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket client = state.workSocket;
            try
            {
                // Read data from the remote device.
                int bytesRead = client.EndReceive(ar);

                Console.WriteLine(bytesRead);

                if (bytesRead > 0)
                {                 
                    byte[] data = state.buffer;

                    var input = Deserialize<DataInput<String, String>>(data);
                    Console.WriteLine(input.input);

                    this.mvm.Message = input.input;

                    // Get the rest of the data.
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    receiveDone.Set();
                }
                Console.WriteLine(state.sb.ToString());
                
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.ToString());
                Receive(client);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);

                Console.WriteLine(String.Format("Connection accepted to {0} ", socket.RemoteEndPoint.ToString()));

                // Signal that the connection has been made.
                connectDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = client.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                // Signal that all bytes have been sent.
                sendDone.Set();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        #endregion



    }
}